#!/usr/bin/env bash
# UnityToFigma 일괄 임포트 실행 헬퍼
#
# - PROJECT_PATH 의 Editor 폴더에 UnityToFigmaBootstrap.cs 가 없으면 복사
# - {PROJECT}/Library/UguiFigmaContext.json 에 URL/PAT/옵션 기록
#   (unity-cli 는 셸 env 를 Unity 프로세스로 전달하지 않으므로 파일 기반 컨텍스트 필수)
# - "Tools/UnityToFigma Bootstrap/Sync Document" 메뉴 실행
# - Console "UnityToFigma import: created=..." 한 줄 요약을 polling
# - 결과 리포트 JSON (Assets/_Temp/UnityToFigmaReport.json) 을 출력
#
# 사용법:
#   FIGMA_DOCUMENT_URL=... FIGMA_PAT=... PROJECT_PATH=/path/to/UnityProject \
#     scripts/run_unity_to_figma_sync.sh
#
# 옵션 환경변수:
#   UGUI_FIGMA_BUILD_PROTOTYPE_FLOW=true    PrototypeFlow 빌드 활성화 (기본 false)
#   UGUI_FIGMA_REPORT_PATH=Assets/...       리포트 출력 경로
#   UGUI_FIGMA_SYNC_TIMEOUT_S=600           Sync 완료 대기 타임아웃 (초)
#   UGUI_FIGMA_KEEP_CONTEXT=true            완료 후 context 파일 유지 (디버깅용; 기본 삭제)
#
# 사전 조건:
#   - unity-cli + Unity bridge 가 동작 중
#   - UnityToFigma 패키지 (com.simonoliver.unitytofigma) 가 설치되어 있음
#   - Unity Editor 가 Edit Mode (Play Mode 아님)

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

# PAT 은 비어있어도 OK. 부트스트랩이 폴백 체인 (ContextFile.pat → 환경변수 →
# EditorPrefs → PlayerPrefs(FIGMA_PERSONAL_ACCESS_TOKEN) → settings asset) 으로 채운다.
# 첫 실행 후에는 PlayerPrefs 에 저장되어 PAT 없이 재실행 가능하다.
if [ -z "$FIGMA_PAT" ]; then
  log "FIGMA_PAT 미지정 → PlayerPrefs/EditorPrefs 폴백 사용 (이전에 한 번 sync 성공한 적이 있어야 함)"
fi

# 1) Bridge 헬스체크 (한 번 빠르게)
unity-cli --timeout-ms=10000 status >/dev/null 2>&1 \
  || fail "Unity bridge 응답 없음. Editor 가 켜져 있는지 확인."

# 2) Bootstrap.cs 복사 보장
SKILL_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BOOTSTRAP_SRC="$SKILL_ROOT/assets/UnityToFigmaBootstrap.cs"
[ -f "$BOOTSTRAP_SRC" ] || fail "스킬에 UnityToFigmaBootstrap.cs 가 없습니다: $BOOTSTRAP_SRC"

# Editor 폴더 결정: Assets/Editor 우선, 없으면 Assets/_Project/Scripts/Editor 생성
if [ -d "$PROJECT_PATH/Assets/Editor" ]; then
  EDITOR_DIR="$PROJECT_PATH/Assets/Editor"
elif [ -d "$PROJECT_PATH/Assets/_Project/Scripts/Editor" ]; then
  EDITOR_DIR="$PROJECT_PATH/Assets/_Project/Scripts/Editor"
else
  EDITOR_DIR="$PROJECT_PATH/Assets/Editor"
  mkdir -p "$EDITOR_DIR"
fi

BOOTSTRAP_DST="$EDITOR_DIR/UnityToFigmaBootstrap.cs"
NEED_REFRESH="no"
if [ ! -f "$BOOTSTRAP_DST" ] || ! cmp -s "$BOOTSTRAP_SRC" "$BOOTSTRAP_DST"; then
  cp "$BOOTSTRAP_SRC" "$BOOTSTRAP_DST"
  log "Bootstrap 복사: $BOOTSTRAP_DST"
  NEED_REFRESH="yes"
fi

if [ "$NEED_REFRESH" = "yes" ]; then
  log "AssetDatabase Refresh + 컴파일 트리거"
  unity-cli --timeout-ms=15000 menu execute path="Assets/Refresh" >/dev/null 2>&1 || true
  # 컴파일 안정화 대기 (큰 프로젝트면 더 걸릴 수 있음)
  sleep 5
fi

# 3) Library/UguiFigmaContext.json 작성
CONTEXT_FILE="$PROJECT_PATH/Library/UguiFigmaContext.json"
mkdir -p "$PROJECT_PATH/Library"

BUILD_PROTO="${UGUI_FIGMA_BUILD_PROTOTYPE_FLOW:-false}"
case "$BUILD_PROTO" in true|TRUE|1) BUILD_PROTO_JSON=true ;; *) BUILD_PROTO_JSON=false ;; esac

KEEP_CTX="${UGUI_FIGMA_KEEP_CONTEXT:-false}"
case "$KEEP_CTX" in true|TRUE|1) KEEP_CTX_JSON=true ;; *) KEEP_CTX_JSON=false ;; esac

REPORT_REL="${UGUI_FIGMA_REPORT_PATH:-Assets/_Temp/UnityToFigmaReport.json}"
TIMEOUT_S="${UGUI_FIGMA_SYNC_TIMEOUT_S:-600}"

# JSON 안전 escape (단순 케이스만 — URL/PAT 에는 backslash 없음을 가정)
json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }
URL_E="$(json_escape "$FIGMA_DOCUMENT_URL")"
PAT_E="$(json_escape "$FIGMA_PAT")"
RPT_E="$(json_escape "$REPORT_REL")"

cat > "$CONTEXT_FILE" <<EOF
{
  "documentUrl": "$URL_E",
  "pat": "$PAT_E",
  "buildPrototypeFlow": $BUILD_PROTO_JSON,
  "reportPath": "$RPT_E",
  "syncTimeoutSeconds": $TIMEOUT_S,
  "keepContext": $KEEP_CTX_JSON
}
EOF
chmod 600 "$CONTEXT_FILE"
log "context 파일 작성: $CONTEXT_FILE"

# 4) Console clear (실패해도 무시)
unity-cli --timeout-ms=15000 console clear >/dev/null 2>&1 || true

# 5) Sync Document 메뉴 실행 (UnityToFigmaImporter.SyncAsync 는 async void 라 즉시 return)
log "Sync 실행: $FIGMA_DOCUMENT_URL"
unity-cli --timeout-ms=60000 menu execute path="Tools/UnityToFigma Bootstrap/Sync Document" >/dev/null \
  || fail "Sync 메뉴 실행 실패. Console 확인."

# 6) Console 에서 "UnityToFigma import: created=" 한 줄 요약을 polling
WAITED=0
INTERVAL=5
SUMMARY=""
TMP_RETRY_DONE="no"
log "임포트 완료 대기 중 (최대 ${TIMEOUT_S}s, ${INTERVAL}s 간격으로 Console polling)"
while [ "$WAITED" -lt "$TIMEOUT_S" ]; do
  sleep "$INTERVAL"
  WAITED=$((WAITED + INTERVAL))
  LOGS_JSON="$(unity-cli --json --timeout-ms=15000 console get 2>/dev/null || true)"
  LINES="$(printf '%s' "$LOGS_JSON" | python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    for m in d.get('result', {}).get('logs', []):
        msg = m.get('message') or ''
        print(msg.replace('\n',' ').strip())
except Exception:
    pass
" 2>/dev/null || true)"

  SUMMARY="$(printf '%s\n' "$LINES" | grep -E '^UnityToFigma import: created=[0-9]+' | tail -n1 || true)"
  if [ -n "$SUMMARY" ]; then
    log "임포트 요약: $SUMMARY"
    break
  fi

  # TMP Essentials 자동 임포트 안내 로그 → 한 번 더 sync 트리거
  if [ "$TMP_RETRY_DONE" = "no" ] && printf '%s\n' "$LINES" | grep -q "TMP Essential Resources 자동 임포트 호출"; then
    log "TMP 자동 임포트 감지 → 컴파일 대기 후 Sync 재실행"
    sleep 10
    unity-cli --timeout-ms=60000 menu execute path="Tools/UnityToFigma Bootstrap/Sync Document" >/dev/null \
      || log "WARN: Sync 재실행 실패"
    TMP_RETRY_DONE="yes"
    continue
  fi

  # 부트스트랩 자체 실패 로그
  if printf '%s\n' "$LINES" | grep -qE '\[UnityToFigmaBootstrap\] Sync 실패|\[UnityToFigmaBootstrap\] PAT|\[UnityToFigmaBootstrap\] Figma document URL'; then
    log "부트스트랩 에러 감지. 최근 로그 출력 후 종료:"
    printf '%s\n' "$LINES" | tail -n 30 >&2
    break
  fi

  # UnityToFigma 자체 에러
  if printf '%s\n' "$LINES" | grep -qE 'Error downloading Figma|Error generating Figma'; then
    log "UnityToFigma 에러 감지. 최근 로그 출력 후 종료:"
    printf '%s\n' "$LINES" | tail -n 30 >&2
    break
  fi
done

if [ -z "$SUMMARY" ]; then
  log "WARN: ${TIMEOUT_S}s 안에 임포트 요약을 받지 못했습니다."
fi

# 7) 리포트 위치 안내 (부트스트랩이 Sync 성공 시 자동 dump)
REPORT_PATH="$PROJECT_PATH/$REPORT_REL"
if [ -f "$REPORT_PATH" ]; then
  log "리포트: $REPORT_PATH"
  cat "$REPORT_PATH"
else
  log "WARN: 리포트 파일 없음 ($REPORT_PATH). Dump Last Report 메뉴를 수동 호출하세요."
fi
