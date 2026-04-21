# UnityToFigma 일괄 임포트 워크플로우

이 문서는 Figma URL 입력 시 사용하는 **1차 자동 임포트 경로**를 정리한다.
에이전트가 노드를 하나씩 분석/생성하지 않고, [UnityToFigma](https://github.com/geuneda/UnityToFigma) 패키지로 한 번에 가져온 뒤
부족한 부분만 보정하는 것이 본 스킬의 기본 전략이다.

## 왜 UnityToFigma 우선인가

이 스킬의 이전 방식(MCP `get_design_context` → `unity-cli ui.*` 로 노드별 생성)은:

- 페이지가 커질수록 명령 횟수가 폭증
- 좌표/사이즈 매핑 실수로 비율이 어긋남 (특히 스트레치 앵커)
- 폰트/이미지 fill/벡터/SDF 도형은 사실상 수동 처리
- 다중 페이지/컴포넌트 재사용 처리가 불가능

UnityToFigma 는 위 문제 대부분을 한 번에 해결한다 (Frame=Screen 프리팹, ImageFill=Sprite, Vector=서버 렌더링,
Component=Prefab, Auto Layout, 폰트 자동 다운로드 등).

## 처리 가능한 영역

| 영역 | 처리 |
| ---- | ---- |
| Frame (화면 루트) | `Assets/Figma/Screens/*.prefab` |
| Components | `Assets/Figma/Components/*.prefab` (인스턴스 참조 자동) |
| Image fills | `Assets/Figma/Textures/*.png` Sprite 임포트 |
| Vector / SDF 도형 (Ellipse, Rectangle, Star) | `UnityToFigma.Runtime.UI.FigmaImage` (SDF) 로 렌더. **`m_CornerRadius` (Vector4) 가 라운드/pill/circle 을 모두 정확히 표현**한다 (예: pill = `{999,999,999,999}`). |
| Vector group / `render` 이름 노드 | 서버 사이드 PNG 렌더 → `Assets/Figma/ServerRenderedImages/` |
| 폰트 | 프로젝트 검색 → 없으면 Google Fonts 다운로드 + TMP Font Asset 자동 생성 |
| Constraints (반응형) | 화면 단위로 자동 매핑 (`Scale` constraint 만 미지원) |
| Auto Layout | `EnableAutoLayout=true` 면 Vertical/Horizontal Layout Group 자동 추가 (실험적) |
| Safe Area | `SafeArea` 이름 노드에 SafeArea 컴포넌트 부여 |

## 한계 (1차 임포트로 안 되는 항목)

이 항목들은 **2차 보정 단계에서 unity-cli/스크립트로 손볼 가치가 있는 후보** 또는 **사용자 안내가 필요한 영역**이다.

| 항목 | 상태 | 본 스킬 정책 |
| ---- | ---- | ----------- |
| Inner Shadow / Layer Blur / Background Blur | 미지원 | 보정 시도하지 않음. 사용자에게 보고. |
| 다중 Fill | 미지원 | 보고만. |
| 단색 외 Stroke 스타일 | 미지원 | 보고만. |
| 이미지 보정 (Exposure/Contrast) | 미지원 | 보고만. |
| Polygon, Boolean, Line/Arrow | 미지원 | 보고만. |
| Star (5개 초과 포인트) | 미지원 | 보고만. |
| `Scale` constraint | 미지원 | 보정 시 다른 anchor 정책으로 우회. |
| **라운드 코너** | UnityToFigma 가 SDF 로 처리. 임포트 결과 그대로 사용. | **추가 라운드 보정/스프라이트 교체 시도 금지.** `references/round-corner-policy.md` 참조. |
| 폰트 매칭 실패 | Console 로그에 `[UnityToFigma]` 로 남음 | 보고만. 사용자에게 폰트 추가 요청. |

## 사전 준비 (다이얼로그 회피)

UnityToFigma 의 `Sync Document` 메뉴는 다음 상황에서 다이얼로그를 띄운다:

1. 설정 에셋이 없을 때 → "Create new settings file?"
2. PAT 이 없을 때 → 입력 다이얼로그
3. TMP Essentials 가 없을 때 → 안내 다이얼로그
4. `BuildPrototypeFlow=true` + `RunTimeAssetsScenePath` 미설정 → "현재 씬을 사용?"
5. 씬 불일치 → "switch scenes?"
6. 플레이 모드일 때 → "exit play mode"

본 스킬은 **부트스트랩 Editor 스크립트** (`assets/UnityToFigmaBootstrap.cs`) 로 위 1·2·3·4 를 사전에 박아 다이얼로그가 안 뜨도록 한다:

- 설정 에셋이 없으면 `Assets/UnityToFigmaSettings.asset` 으로 자동 생성
- `DocumentUrl` / `pat` 은 우선순위에 따라 해소: **ContextFile (`{PROJECT}/Library/UguiFigmaContext.json`)** > 환경변수 > EditorPrefs > Settings/PlayerPrefs (PAT 폴백)
  - unity-cli 는 셸 env 를 Editor 에 전달하지 않으므로 자동화에서는 ContextFile 만 신뢰 가능
- `PlayerPrefs("FIGMA_PERSONAL_ACCESS_TOKEN")` 에 PAT 직접 set
- `BuildPrototypeFlow=false` 강제 (PrototypeFlow 가 필요하면 ContextFile 의 `buildPrototypeFlow:true`)
- `OnlyImportSelectedPages=false` (자동화에서는 페이지 다이얼로그 회피)
- TMP Essentials 가 없으면 `TMPro.TMP_PackageResourceImporter.ImportResources(true,false,false)` 로 자동 임포트 시도. 호출 후 컴파일 안정화를 위해 Sync 한 번 더 트리거 (sync 스크립트가 자동 처리)

6 (Play mode) 은 부트스트랩이 강제할 수 없다. Editor 가 Play 모드면 사용자에게 stop 요청 후 진행.

## 실행 흐름 (자동화)

```bash
# 1. Bridge / 패키지 설치 보장
unity-cli status
bash scripts/ensure_unity_to_figma_package.sh

# 2. 부트스트랩 + 동기화 (env 는 셸 → 스크립트 전달용. 스크립트가 ContextFile 로 변환해 Editor 에 전달)
PROJECT_PATH=/abs/path/to/UnityProject \
FIGMA_DOCUMENT_URL=https://www.figma.com/design/.../... \
FIGMA_PAT=figd_xxx \
bash scripts/run_unity_to_figma_sync.sh
```

내부적으로 다음 단계가 일어난다:

1. `assets/UnityToFigmaBootstrap.cs` → `$PROJECT/Assets/Editor/` (없으면 `_Project/Scripts/Editor`) 복사
2. `unity-cli menu execute path="Assets/Refresh"` 로 컴파일 트리거 (5초 안정화 대기)
3. `$PROJECT/Library/UguiFigmaContext.json` 작성 (URL/PAT/옵션, chmod 600)
4. `unity-cli console clear`
5. `unity-cli menu execute path="Tools/UnityToFigma Bootstrap/Sync Document"`
   - 내부 동작: ContextFile 로드 → PrepareSettings → TMP Essentials 체크/자동 임포트 → Reflection 으로 `UnityToFigma.Editor.UnityToFigmaImporter.Sync()` 직접 호출 (ExecuteMenuItem 폴백 보유)
   - `Application.logMessageReceived` 후킹으로 `"UnityToFigma import: ..."` 한 줄을 잡으면 즉시 완료 처리 + 자동 dump
6. 셸 측은 5초 간격 polling 으로 완료 라인 또는 에러 라인을 감지 (최대 600초, `UGUI_FIGMA_SYNC_TIMEOUT_S` 로 조절)
7. 완료 후 ContextFile 자동 삭제 (PAT 보안). `UGUI_FIGMA_KEEP_CONTEXT=true` 면 유지.
8. `Assets/_Temp/UnityToFigmaReport.json` 출력 (importRoot, screens/components/pages/textures/fonts, roundedHandled/roundedExtreme/roundedSkipped)

## Console 임포트 요약 파싱

UnityToFigma 가 끝나면 Console 에 다음 한 줄 요약이 남는다:

```
UnityToFigma import: created=N, updated=N, skipped=N, failed=N, orphaned=N, manifestRemoved=N
```

부트스트랩이 In-Editor 에서 `Application.logMessageReceived` 로 직접 잡으므로 셸 측 polling 은 보조 역할.
필요하면 `unity-cli --json console get` 의 `result.logs[].message` 에서 정규식 매칭:

```
^UnityToFigma import: created=(?<c>\d+), updated=(?<u>\d+), skipped=(?<s>\d+), failed=(?<f>\d+), orphaned=(?<o>\d+), manifestRemoved=(?<m>\d+)
```

> 주의: `console get` 응답은 `{message, timestamp}` 만 포함하고 **`logType`/`stackTrace` 는 없다.** 에러/경고 분류는 메시지 문자열 패턴 매칭으로 한다.

추가 메시지는 `[UnityToFigma]` 접두로 들어온다 (폰트 매칭 실패, 다운로드 실패 등). 이 줄은 그대로 사용자 보고서에 포함한다.

`failed > 0` 이면 임포트 자체가 부분 실패이므로 워크플로우를 중단하고 사용자에게 PAT/네트워크/문서 권한을 확인하도록 한다.

## 보정 (Patch) 단계 원칙

자동 임포트 후 에이전트가 Figma 스크린샷과 Game View 캡처를 비교한 뒤 **보정만 수행**한다.
보정 단계 행동 원칙:

1. **UnityToFigma 가 만든 RectTransform/앵커는 가급적 건드리지 않는다.** 화면 단위 비율은 constraints 로 이미 잡혀 있다.
2. 보정이 필요한 항목은 다음으로 한정:
   - 폰트 매칭 실패 → 프로젝트 폰트로 교체 (`component update type=TMPro.TextMeshProUGUI values={"font": ...}`)
   - 색상 mismatch (디자인 토큰 변경 등) → `component update type=UnityEngine.UI.Image values={"color": ...}`
   - 스크린별로 실제로 빠진 텍스트/이미지 → 새로 추가
   - SafeArea 누락 → SafeArea 컴포넌트 추가
3. **금지 항목**:
   - 라운드 코너를 별도 스프라이트로 교체하거나 Mask 로 흉내내려는 시도 (`round-corner-policy.md` 참조)
   - 자동 임포트된 SDF 도형의 형상 수동 변경
   - Constraints 를 무시한 절대좌표 재배치

## 다중 페이지

UnityToFigma 는 모든 페이지를 한 번에 가져온다. 페이지가 너무 많으면:

- `OnlyImportSelectedPages=true` 로 설정 후 사용자에게 어떤 페이지를 임포트할지 선택 받기
- 본 스킬 자동화에서는 기본 false. 다중 페이지가 필요하면 사용자가 명시적으로 요청한 뒤 `UGUI_FIGMA_SELECTED_PAGES` 환경변수를 통해 처리 (TODO).

## 재임포트 정책

| 정책 | 의미 |
| ---- | ---- |
| `PathUpdatePolicy.KeepExistingAssetPath` (기본) | Figma 트리 변경 시에도 기존 프리팹 경로 유지 |
| `PathUpdatePolicy.MoveToLatestResolvedPath` | 최신 규칙 경로로 이동 |
| `MissingNodePolicy.MarkAsOrphaned` (기본) | 사라진 노드는 매니페스트에 고아로 표시 |
| `MissingNodePolicy.DeleteOnImport` | 매니페스트 행 즉시 제거 (디스크 파일은 자동 삭제 안 함) |

본 스킬은 기본값을 그대로 두고, 디스크에 남은 고아 파일은 사용자에게 정리 요청만 한다.

## 트러블슈팅

| 증상 | 원인/해결 |
| ---- | --------- |
| `Sync Document` 메뉴 실행이 즉시 끝나는데 임포트 결과가 없음 | PAT 누락 → PlayerPrefs 확인. `Tools/UnityToFigma Bootstrap/Prepare Settings` 먼저 호출되었는지 확인. |
| Console 에 "Error downloading Figma document" | PAT 권한/문서 URL 오류. `FigmaApiUtils.GetFigmaDocumentIdFromUrl` 가 fileId 추출 못 함. |
| TextMeshPro 안내 다이얼로그 발생 | 부트스트랩이 자동 임포트 시도 (`TMPro.TMP_PackageResourceImporter.ImportResources`). 실패 시: `Window > TextMeshPro > Import TMP Essential Resources`. |
| `Please exit play mode` | Editor 가 Play 모드. 사용자에게 stop 요청. |
| 환경변수로 URL/PAT 을 줘도 부트스트랩이 못 읽음 | unity-cli 는 셸 env 를 Editor 프로세스에 전달하지 않음. **`run_unity_to_figma_sync.sh` 가 자동으로 `Library/UguiFigmaContext.json` 을 만들어 전달.** 직접 호출 시 ContextFile 직접 작성 필요. |
| 임포트 후 화면이 통째로 뒤섞임 | Figma 문서의 Section 안에 모든 페이지가 들어가 있을 수 있음. 정상이며 PrototypeFlow 활성화 시 의도대로 보임. |
| 폰트가 깨져 보임 | 폰트 매칭 실패 ([UnityToFigma] 메시지 확인). 프로젝트에 TTF 추가 또는 동일 이름 폰트로 교체. |
