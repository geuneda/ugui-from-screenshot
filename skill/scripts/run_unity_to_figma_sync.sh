#!/usr/bin/env bash
# UnityToFigma 일괄 임포트 실행 헬퍼
# - PROJECT_PATH 의 Editor 폴더에 UnityToFigmaBootstrap.cs 가 없으면 복사
# - FIGMA_DOCUMENT_URL / FIGMA_PAT 을 EditorPrefs 또는 환경변수로 전달
# - Tools/UnityToFigma Bootstrap/Sync Document 메뉴 실행
# - 결과 리포트 JSON (Assets/_Temp/UnityToFigmaReport.json) 을 dump 하고 stdout 에 출력
#
# 사용법:
#   FIGMA_DOCUMENT_URL=... FIGMA_PAT=... PROJECT_PATH=/path/to/UnityProject \
#     scripts/run_unity_to_figma_sync.sh
#
#   (선택) UGUI_FIGMA_BUILD_PROTOTYPE_FLOW=true  PrototypeFlow 빌드 활성화
#
# 사전 조건:
#   - unity-cli + Unity bridge 가 동작 중
#   - UnityToFigma 패키지가 설치되어 있음 (없으면 ensure_unity_to_figma_package.sh 실행)
#   - TextMeshPro Essential Resources 가 임포트되어 있음

set -u
set -o pipefail

log()  { printf '[u2f-sync] %s\n' "$*" >&2; }
fail() { log "ERROR: $*"; exit 2; }

command -v unity-cli >/dev/null 2>&1 || fail "unity-cli 가 PATH 에 없습니다."

PROJECT_PATH="${PROJECT_PATH:-}"
[ -n "$PROJECT_PATH" ] || fail "PROJECT_PATH 환경변수가 필요합니다 (Unity 프로젝트 루트)."
[ -d "$PROJECT_PATH/Assets" ] || fail "유효한 Unity 프로젝트가 아닙니다: $PROJECT_PATH"

FIGMA_DOCUMENT_URL="${FIGMA_DOCUMENT_URL:-}"
FIGMA_PAT="${FIGMA_PAT:-}"
[ -n "$FIGMA_DOCUMENT_URL" ] || fail "FIGMA_DOCUMENT_URL 환경변수가 필요합니다."
[ -n "$FIGMA_PAT" ] || fail "FIGMA_PAT 환경변수가 필요합니다."

# 1) Bootstrap.cs 복사 보장
SKILL_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BOOTSTRAP_SRC="$SKILL_ROOT/assets/UnityToFigmaBootstrap.cs"
[ -f "$BOOTSTRAP_SRC" ] || fail "스킬에 UnityToFigmaBootstrap.cs 가 없습니다: $BOOTSTRAP_SRC"

EDITOR_DIR="$PROJECT_PATH/Assets/_Project/Scripts/Editor"
BOOTSTRAP_DST="$EDITOR_DIR/UnityToFigmaBootstrap.cs"

mkdir -p "$EDITOR_DIR"
if [ ! -f "$BOOTSTRAP_DST" ] || ! cmp -s "$BOOTSTRAP_SRC" "$BOOTSTRAP_DST"; then
  cp "$BOOTSTRAP_SRC" "$BOOTSTRAP_DST"
  log "Bootstrap 복사: $BOOTSTRAP_DST"
  unity-cli --timeout-ms=15000 editor refresh >/dev/null 2>&1 || true
  unity-cli --timeout-ms=300000 editor compile >/dev/null 2>&1 || log "WARN: editor compile 응답 지연"
fi

# 2) Bridge 헬스체크
unity-cli --timeout-ms=10000 status >/dev/null 2>&1 \
  || fail "Unity bridge 응답 없음. Editor 가 켜져 있는지 확인."

# 3) Sync Document 실행 (env 가 자식 프로세스로 전달되지 않으므로 EditorPrefs 로 한 번 박는 형태도 지원)
log "Sync 실행: $FIGMA_DOCUMENT_URL"
export FIGMA_DOCUMENT_URL FIGMA_PAT UGUI_FIGMA_BUILD_PROTOTYPE_FLOW="${UGUI_FIGMA_BUILD_PROTOTYPE_FLOW:-false}"

# Sync 시작 전 Console 비워두면 polling 이 깔끔하다 (실패해도 무시)
unity-cli --timeout-ms=15000 console clear >/dev/null 2>&1 || true

# 메뉴 실행 (UnityToFigmaImporter.SyncAsync 는 async void 라서 즉시 return 됨)
unity-cli --timeout-ms=60000 menu execute path="Tools/UnityToFigma Bootstrap/Sync Document" \
  || fail "Sync 메뉴 실행 실패. Console 로그 확인."

# 4) Console 에서 "UnityToFigma import: created=" 한 줄 요약을 polling (최대 SYNC_TIMEOUT_S 초)
SYNC_TIMEOUT_S="${UGUI_FIGMA_SYNC_TIMEOUT_S:-600}"
WAITED=0
INTERVAL=5
SUMMARY=""
log "임포트 완료 대기 중 (최대 ${SYNC_TIMEOUT_S}s, ${INTERVAL}s 간격으로 Console polling)"
while [ "$WAITED" -lt "$SYNC_TIMEOUT_S" ]; do
  sleep "$INTERVAL"
  WAITED=$((WAITED + INTERVAL))
  LOGS="$(unity-cli --timeout-ms=15000 console get 2>/dev/null || true)"
  SUMMARY="$(printf '%s\n' "$LOGS" | grep -E '^UnityToFigma import: created=[0-9]+' | tail -n1 || true)"
  if [ -n "$SUMMARY" ]; then
    log "임포트 요약: $SUMMARY"
    break
  fi
  # 명백한 에러 로그가 있으면 조기 종료
  if printf '%s\n' "$LOGS" | grep -qE '\[UnityToFigma\] Error|UnityToFigma Error'; then
    log "Console 에 UnityToFigma 에러 로그 감지. 즉시 종료."
    printf '%s\n' "$LOGS" | tail -n 30 >&2
    break
  fi
done

if [ -z "$SUMMARY" ]; then
  log "WARN: ${SYNC_TIMEOUT_S}s 안에 임포트 요약을 받지 못했습니다. (대형 문서면 UGUI_FIGMA_SYNC_TIMEOUT_S 를 늘리세요)"
fi

# 5) 리포트 dump
unity-cli --timeout-ms=60000 menu execute path="Tools/UnityToFigma Bootstrap/Dump Last Report" \
  || log "WARN: 리포트 dump 메뉴 실행 실패"

REPORT_PATH="$PROJECT_PATH/Assets/_Temp/UnityToFigmaReport.json"
if [ -f "$REPORT_PATH" ]; then
  log "리포트 위치: $REPORT_PATH"
  cat "$REPORT_PATH"
else
  log "WARN: 리포트 파일이 생성되지 않았습니다: $REPORT_PATH"
fi
