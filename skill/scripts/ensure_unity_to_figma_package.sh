#!/usr/bin/env bash
# UnityToFigma 패키지 설치 보장 스크립트
# - Unity 프로젝트에 com.simonoliver.unitytofigma 패키지가 설치되어 있는지 확인
# - 없으면 git URL 로 추가 (unity-cli package add)
# - 의존(TextMeshPro, Newtonsoft Json) 도 함께 보장
#
# 사용법:
#   scripts/ensure_unity_to_figma_package.sh           # auto: 미설치면 설치
#   scripts/ensure_unity_to_figma_package.sh --check   # 상태만 확인 (0=설치됨, 1=미설치, 2=오류)
#   scripts/ensure_unity_to_figma_package.sh --update  # 항상 최신 git HEAD 로 갱신
#
# 사전 조건: unity-cli 가 PATH 에 있어야 하고 Unity Editor + bridge 가 실행 중이어야 한다.

set -u
set -o pipefail

PKG_NAME="com.simonoliver.unitytofigma"
PKG_GIT_URL="https://github.com/geuneda/UnityToFigma.git"
PKG_DEPS=("com.unity.nuget.newtonsoft-json" "com.unity.textmeshpro")

MODE="auto"
for arg in "$@"; do
  case "$arg" in
    --check)  MODE="check" ;;
    --update) MODE="update" ;;
    -h|--help) sed -n '2,16p' "$0"; exit 0 ;;
    *) printf '[u2f-pkg] WARN: 알 수 없는 인자: %s\n' "$arg" >&2 ;;
  esac
done

log()  { printf '[u2f-pkg] %s\n' "$*" >&2; }
fail() { log "ERROR: $*"; exit 2; }

command -v unity-cli >/dev/null 2>&1 || fail "unity-cli 가 PATH 에 없습니다. scripts/ensure_unity_cli.sh 를 먼저 실행하세요."

# 1) Bridge 살아있는지 빠르게 확인
if ! unity-cli --timeout-ms=5000 status >/dev/null 2>&1; then
  fail "Unity bridge 가 응답하지 않습니다. Unity Editor 가 켜져 있고 unity-connector 가 로드되었는지 확인하세요."
fi

# 2) 현재 패키지 목록 조회 (JSON 사용)
PKG_LIST="$(unity-cli --json --timeout-ms=30000 package list 2>&1 || true)"

is_installed() {
  local needle="$1"
  printf '%s' "$PKG_LIST" | grep -q "\"$needle\""
}

INSTALLED="no"
if is_installed "$PKG_NAME"; then
  INSTALLED="yes"
fi

case "$MODE" in
  check)
    if [ "$INSTALLED" = "yes" ]; then
      log "설치되어 있음: $PKG_NAME"
      exit 0
    fi
    log "미설치: $PKG_NAME"
    exit 1
    ;;
  auto)
    if [ "$INSTALLED" = "yes" ]; then
      log "이미 설치되어 있음: $PKG_NAME"
      exit 0
    fi
    ;;
  update)
    log "update 모드: $PKG_NAME 를 git HEAD 로 (재)설치합니다."
    ;;
esac

# 3) 의존 패키지 먼저 설치 (실패해도 무시 — 보통 manifest 가 자동으로 끌어옴)
for dep in "${PKG_DEPS[@]}"; do
  if is_installed "$dep"; then
    log "의존 OK: $dep"
  else
    log "의존 추가: $dep"
    if ! unity-cli --timeout-ms=120000 package add name="$dep" >/dev/null 2>&1; then
      log "WARN: 의존 추가 실패 (계속 진행): $dep"
    fi
  fi
done

# 4) 본 패키지 추가 (git URL)
log "패키지 추가: $PKG_NAME ← $PKG_GIT_URL"
if ! unity-cli --timeout-ms=180000 package add name="$PKG_NAME" url="$PKG_GIT_URL" >/dev/null 2>&1; then
  # name 인자를 받지 않는 브릿지 버전을 위한 폴백
  if ! unity-cli --timeout-ms=180000 package add identifier="$PKG_GIT_URL" >/dev/null 2>&1; then
    if ! unity-cli --timeout-ms=180000 package add "$PKG_GIT_URL" >/dev/null 2>&1; then
      fail "package add 실패. Unity Editor Console 로그를 확인하세요."
    fi
  fi
fi

# 5) compile 보장
log "edit 후 컴파일 트리거"
unity-cli --timeout-ms=120000 editor refresh >/dev/null 2>&1 || true
unity-cli --timeout-ms=300000 editor compile >/dev/null 2>&1 || log "WARN: editor compile 응답이 늦거나 실패"

# 6) 재확인
PKG_LIST="$(unity-cli --json --timeout-ms=30000 package list 2>&1 || true)"
if printf '%s' "$PKG_LIST" | grep -q "\"$PKG_NAME\""; then
  log "설치 완료: $PKG_NAME"
  exit 0
fi

fail "설치 후에도 $PKG_NAME 이 패키지 목록에 보이지 않습니다."
