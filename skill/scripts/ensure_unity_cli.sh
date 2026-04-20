#!/usr/bin/env bash
# unity-cli 설치 보장 스크립트
# - PATH에 unity-cli 가 있는지 확인
# - 없으면 https://github.com/geuneda/unity-cli 를 클론하고 dotnet publish 로
#   self-contained 단일 실행 파일을 빌드한 뒤 ~/.local/bin/unity-cli 심볼릭 링크 생성
#
# 사용법:
#   scripts/ensure_unity_cli.sh           # auto: 없으면 설치, 있으면 그대로 종료
#   scripts/ensure_unity_cli.sh --check   # 상태만 검사 (0=있음, 1=없음, 2=오류)
#   scripts/ensure_unity_cli.sh --force   # 이미 있어도 다시 빌드/링크
#   scripts/ensure_unity_cli.sh --update  # 레포 최신으로 갱신 후 재빌드 (있어도)
#
# 환경변수:
#   UNITY_CLI_INSTALL_DIR   클론/빌드 디렉토리 (기본: $HOME/.local/share/unity-cli)
#   UNITY_CLI_BIN_DIR       심볼릭 링크 위치 (기본: $HOME/.local/bin)
#
# 의존: bash, git, dotnet (net10.0+ SDK)

set -u
set -o pipefail

REPO_URL="https://github.com/geuneda/unity-cli.git"
BRANCH="main"
INSTALL_DIR="${UNITY_CLI_INSTALL_DIR:-$HOME/.local/share/unity-cli}"
BIN_DIR="${UNITY_CLI_BIN_DIR:-$HOME/.local/bin}"
TARGET_LINK="$BIN_DIR/unity-cli"

MODE="auto"  # auto | check | force | update
for arg in "$@"; do
  case "$arg" in
    --check)  MODE="check" ;;
    --force)  MODE="force" ;;
    --update) MODE="update" ;;
    -h|--help)
      sed -n '2,18p' "$0"; exit 0 ;;
    *)
      printf '[unity-cli-install] WARN: 알 수 없는 인자 무시: %s\n' "$arg" >&2 ;;
  esac
done

log()  { printf '[unity-cli-install] %s\n' "$*" >&2; }
fail() { log "ERROR: $*"; exit 2; }

# ---------------------------------------------------------------------------
# 0) 이미 설치되어 있는지 검사
# ---------------------------------------------------------------------------
EXISTING_PATH=""
if command -v unity-cli >/dev/null 2>&1; then
  EXISTING_PATH="$(command -v unity-cli)"
fi

case "$MODE" in
  check)
    if [ -n "$EXISTING_PATH" ]; then
      log "설치되어 있음: $EXISTING_PATH"
      exit 0
    fi
    log "unity-cli 가 PATH 에 없습니다."
    exit 1
    ;;
  auto)
    if [ -n "$EXISTING_PATH" ]; then
      log "이미 설치되어 있습니다: $EXISTING_PATH"
      exit 0
    fi
    ;;
  force|update)
    if [ -n "$EXISTING_PATH" ]; then
      log "기존 설치 확인됨($EXISTING_PATH) → $MODE 모드로 재빌드합니다."
    fi
    ;;
esac

# ---------------------------------------------------------------------------
# 1) 종속성 검사
# ---------------------------------------------------------------------------
command -v git >/dev/null 2>&1 || fail "git 이 필요합니다."
if ! command -v dotnet >/dev/null 2>&1; then
  cat >&2 <<'EOF'
[unity-cli-install] ERROR: .NET SDK(net10.0 이상)가 필요합니다.
  macOS:   brew install --cask dotnet-sdk
  설치 가이드: https://dotnet.microsoft.com/download
설치 후 새 셸을 열고 다시 실행하세요.
EOF
  exit 2
fi

DOTNET_VER="$(dotnet --version 2>/dev/null || echo unknown)"
log ".NET SDK 감지됨: $DOTNET_VER"

# net10 미만이면 csproj 가 net10.0 을 요구하므로 빌드가 실패한다. 경고만 띄움.
case "$DOTNET_VER" in
  10.*|preview*|*-preview*) ;;
  unknown) log "주의: .NET 버전을 식별하지 못했습니다." ;;
  *)
    log "주의: 감지된 .NET SDK($DOTNET_VER)는 unity-cli(net10.0+) 요구사항보다 낮을 수 있습니다."
    log "      빌드가 실패하면 .NET 10 SDK 를 설치한 뒤 다시 시도하세요."
    ;;
esac

# ---------------------------------------------------------------------------
# 2) 레포 클론 / 갱신
# ---------------------------------------------------------------------------
mkdir -p "$INSTALL_DIR" "$BIN_DIR"

if [ -d "$INSTALL_DIR/.git" ]; then
  if [ "$MODE" = "update" ] || [ "$MODE" = "force" ]; then
    log "기존 클론 갱신 중: $INSTALL_DIR"
    (cd "$INSTALL_DIR" && git fetch --depth=1 origin "$BRANCH" && git reset --hard "origin/$BRANCH") \
      || fail "git 업데이트 실패"
  else
    log "기존 클론 사용: $INSTALL_DIR (갱신을 원하면 --update)"
  fi
else
  log "클론 중: $REPO_URL → $INSTALL_DIR"
  rm -rf "$INSTALL_DIR"
  git clone --depth=1 --branch "$BRANCH" "$REPO_URL" "$INSTALL_DIR" || fail "git clone 실패"
fi

CSPROJ="$INSTALL_DIR/src/UnityCli/UnityCli.csproj"
[ -f "$CSPROJ" ] || fail "프로젝트 파일을 찾을 수 없습니다: $CSPROJ"

# ---------------------------------------------------------------------------
# 3) 빌드 RID 결정
# ---------------------------------------------------------------------------
OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS-$ARCH" in
  Darwin-arm64)        RID="osx-arm64" ;;
  Darwin-x86_64)       RID="osx-x64" ;;
  Linux-x86_64)        RID="linux-x64" ;;
  Linux-aarch64)       RID="linux-arm64" ;;
  *) fail "지원하지 않는 플랫폼: $OS-$ARCH (수동 빌드 필요)" ;;
esac
log "타겟 RID: $RID"

# ---------------------------------------------------------------------------
# 4) dotnet publish (self-contained, 단일 파일)
# ---------------------------------------------------------------------------
PUBLISH_DIR="$INSTALL_DIR/publish/$RID"
LOG_FILE="$(mktemp -t unity-cli-publish.XXXXXX.log)"
log "빌드 중 (dotnet publish, 시간이 걸릴 수 있습니다)..."
log "  로그: $LOG_FILE"

if ! dotnet publish "$CSPROJ" \
      -c Release \
      -r "$RID" \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -o "$PUBLISH_DIR" \
      >"$LOG_FILE" 2>&1; then
  log "빌드 실패. 마지막 30줄:"
  tail -n 30 "$LOG_FILE" >&2
  exit 2
fi

BINARY="$PUBLISH_DIR/UnityCli"
[ -x "$BINARY" ] || fail "빌드 산출물이 없습니다: $BINARY"

# ---------------------------------------------------------------------------
# 5) 심볼릭 링크 생성
# ---------------------------------------------------------------------------
ln -sfn "$BINARY" "$TARGET_LINK"
chmod +x "$BINARY"
log "설치 완료: $TARGET_LINK → $BINARY"

# ---------------------------------------------------------------------------
# 6) PATH 안내
# ---------------------------------------------------------------------------
case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *)
    log "주의: $BIN_DIR 가 현재 셸의 PATH 에 없습니다."
    log "      쉘 설정(~/.zshrc 등)에 다음 줄을 추가하세요:"
    log "        export PATH=\"$BIN_DIR:\$PATH\""
    ;;
esac

# ---------------------------------------------------------------------------
# 7) 동작 확인
# ---------------------------------------------------------------------------
if "$BINARY" >/dev/null 2>&1 || [ "$?" = 1 ]; then
  log "OK: $BINARY 실행 확인 (Unity 브리지 미연결이어도 정상)"
else
  log "주의: $BINARY 실행 검증 실패. 위 빌드 로그를 확인하세요."
fi

exit 0
