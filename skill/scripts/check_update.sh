#!/usr/bin/env bash
# ugui-from-screenshot 스킬 자동 업데이트 체커
# 로컬 .commit-hash와 원격 main HEAD를 비교하여, 다르면 skill/ 하위만 내려받아 로컬을 갱신한다.
#
# 사용법:
#   scripts/check_update.sh             # 인터랙티브 (변경 있으면 확인 후 적용)
#   scripts/check_update.sh --auto      # 확인 없이 자동 업데이트
#   scripts/check_update.sh --check     # 버전만 비교하고 종료 (0=최신, 1=업데이트 있음, 2=오류)
#
# 의존: bash, curl, tar. gh가 있으면 gh 우선 사용.

set -u
set -o pipefail

REPO="geuneda/ugui-from-screenshot"
BRANCH="main"
REMOTE_SUBDIR="skill"

# 스킬 루트 = 이 스크립트의 상위의 상위
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
HASH_FILE="$SKILL_ROOT/.commit-hash"

MODE="interactive"
for arg in "$@"; do
  case "$arg" in
    --auto)  MODE="auto" ;;
    --check) MODE="check" ;;
    -h|--help)
      sed -n '2,12p' "$0"; exit 0 ;;
  esac
done

log()  { printf '[ugui-update] %s\n' "$*" >&2; }
fail() { log "ERROR: $*"; exit 2; }

fetch_remote_sha() {
  if command -v gh >/dev/null 2>&1; then
    gh api "repos/$REPO/commits/$BRANCH" --jq '.sha' 2>/dev/null && return 0
  fi
  curl -fsSL "https://api.github.com/repos/$REPO/commits/$BRANCH" \
    | sed -n 's/^[[:space:]]*"sha"[[:space:]]*:[[:space:]]*"\([0-9a-f]\{40\}\)".*/\1/p' \
    | head -n1
}

REMOTE_SHA="$(fetch_remote_sha)"
[ -n "${REMOTE_SHA:-}" ] || fail "원격 커밋 해시를 가져오지 못했습니다."

LOCAL_SHA=""
[ -f "$HASH_FILE" ] && LOCAL_SHA="$(tr -d ' \n\r' < "$HASH_FILE")"

if [ "$LOCAL_SHA" = "$REMOTE_SHA" ]; then
  log "이미 최신입니다 ($REMOTE_SHA)."
  [ "$MODE" = "check" ] && exit 0
  exit 0
fi

log "업데이트 있음: 로컬=${LOCAL_SHA:-없음} → 원격=$REMOTE_SHA"
if [ "$MODE" = "check" ]; then
  exit 1
fi

if [ "$MODE" = "interactive" ]; then
  printf '로컬을 원격 최신으로 업데이트할까요? [y/N] '
  read -r ans || ans=""
  case "$ans" in y|Y|yes|YES) ;; *) log "취소했습니다."; exit 0 ;; esac
fi

TMP="$(mktemp -d -t ugui-update.XXXXXX)"
trap 'rm -rf "$TMP"' EXIT

TARBALL="$TMP/src.tar.gz"
if command -v gh >/dev/null 2>&1; then
  gh api "repos/$REPO/tarball/$REMOTE_SHA" > "$TARBALL" 2>/dev/null \
    || curl -fsSL "https://api.github.com/repos/$REPO/tarball/$REMOTE_SHA" -o "$TARBALL"
else
  curl -fsSL "https://api.github.com/repos/$REPO/tarball/$REMOTE_SHA" -o "$TARBALL"
fi
[ -s "$TARBALL" ] || fail "tarball 다운로드 실패"

mkdir -p "$TMP/extract"
tar -xzf "$TARBALL" -C "$TMP/extract" || fail "tar 추출 실패"

TOP="$(find "$TMP/extract" -mindepth 1 -maxdepth 1 -type d | head -n1)"
SRC="$TOP/$REMOTE_SUBDIR"
[ -d "$SRC" ] || fail "원격에 $REMOTE_SUBDIR/ 디렉토리가 없습니다: $SRC"

# 백업 후 교체: SKILL.md, references/, scripts/, assets/ 만 동기화 (다른 로컬 파일 보존)
BACKUP="$SKILL_ROOT/.backup-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$BACKUP"
for item in SKILL.md references scripts assets; do
  if [ -e "$SKILL_ROOT/$item" ]; then
    cp -R "$SKILL_ROOT/$item" "$BACKUP/" 2>/dev/null || true
  fi
done
log "백업: $BACKUP"

# 동기화: 원격에 있는 항목만 복사 (로컬 커스텀 파일은 덮어쓰지 않음)
for item in SKILL.md references scripts assets; do
  if [ -e "$SRC/$item" ]; then
    rm -rf "$SKILL_ROOT/$item"
    cp -R "$SRC/$item" "$SKILL_ROOT/$item"
  fi
done

printf '%s\n' "$REMOTE_SHA" > "$HASH_FILE"
log "업데이트 완료: $REMOTE_SHA"
log "문제 시 복원: cp -R $BACKUP/* $SKILL_ROOT/"
