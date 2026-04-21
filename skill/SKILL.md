---
name: ugui-from-screenshot
description: UI 디자인을 기반으로 UGUI를 자동 구성하는 스킬. Figma URL은 UnityToFigma 패키지로 일괄 포팅한 뒤 부족한 부분만 unity-cli로 보정하고, 스크린샷은 비전+unity-cli로 구성한다. 라운드 코너는 자동 보정하지 않고 사용자에게 보고한다. 'UGUI from screenshot', 'UGUI from Figma', '스크린샷으로 UI', 'UI 구현', 'screenshot to UGUI', '스크린샷 기반 UI', 'Figma로 UGUI', 'Figma UI 구현' 등의 요청 시 트리거된다.
metadata:
  mcp-server: figma
---

# UGUI from Design

UI 디자인을 Unity UGUI 로 옮기는 스킬. **Figma URL 입력 시에는 [UnityToFigma](https://github.com/geuneda/UnityToFigma) 패키지로 화면/컴포넌트/이미지/폰트/벡터를 한 번에 포팅**하고, 자동 임포트가 처리하지 못한 부분만 unity-cli 로 보정한다. 스크린샷 입력 시에는 비전 분석 + unity-cli 로 구성한다.

## 핵심 원칙 (전역)

1. **자동 임포트 우선, 보정 최소화**. UnityToFigma 가 처리한 RectTransform/앵커/색상은 가급적 건드리지 않는다. 비율을 망치는 가장 흔한 원인이다.
2. **라운드 코너는 추가 보정/대체를 하지 않는다**. Path A (UnityToFigma) 에서는 `FigmaImage.cornerRadius` 가 SDF 로 정확히 처리되므로 그대로 둔다. Path B (스크린샷) 에서는 라운드를 사용자에게 위임한다. **어떤 경우에도 sprite/mask 로 라운드를 흉내내지 말 것.** 자세한 규칙: `references/round-corner-policy.md`
3. **에이전트는 차이만 처리**. 자동 임포트가 만든 결과 vs 레퍼런스 스크린샷을 비교해서 실제로 빠진/잘못된 항목만 수정한다. 전체를 다시 만들지 않는다.
4. **다이얼로그가 뜨는 작업은 자동화하지 말 것**. 다이얼로그를 띄우는 환경(설정 누락, PAT 미입력, TMP Essentials 미설치 등)은 사전 부트스트랩으로 박아두거나 사용자에게 한 번 요청한다.

## Prerequisites

- unity-cli (없으면 Phase −2 에서 자동 설치)
- .NET SDK net10.0+ (unity-cli 빌드용. macOS: `brew install --cask dotnet-sdk`)
- Unity Editor 에 unity-connector 패키지 설치
- 브릿지 상태 확인: `unity-cli status`
- Figma URL 입력 시:
  - UnityToFigma 패키지 (`com.simonoliver.unitytofigma`) -- Phase 0 에서 자동 보장
  - Figma Personal Access Token (PAT) -- 사용자에게 1회 입력 요청
  - TextMeshPro Essential Resources 임포트
  - (선택) Figma MCP 서버 -- 검증 단계용 스크린샷 비교에만 사용

## Repository & Auto-Update

이 스킬은 [geuneda/ugui-from-screenshot](https://github.com/geuneda/ugui-from-screenshot) 레포의 `skill/` 디렉토리와 동기화된다. 다른 PC에서 개선 후 push하면 여기서 `check_update.sh`로 끌어올 수 있다.

- 레포: `https://github.com/geuneda/ugui-from-screenshot` (public, `main` 브랜치)
- 원격 스킬 경로: `skill/`
- 로컬 버전 파일: `.commit-hash` (현재 반영된 원격 커밋 SHA)

### Phase −1: 업데이트 체크 (스킬 실행 시 최우선 수행)

Phase 0 이전에 **반드시** 업데이트 여부를 확인한다. 원격 main HEAD가 로컬 `.commit-hash`와 다르면 최신 변경을 반영한 뒤 워크플로우를 시작해야 한다.

**자동 체크 & 적용**:

```bash
# 스킬 루트에서
bash scripts/check_update.sh --auto
```

- `--auto`: 변경이 있으면 확인 없이 자동 업데이트 (권장, 비파괴적 — 기존 파일은 `.backup-<timestamp>/` 로 보관)
- 옵션 없이 실행하면 인터랙티브(y/N 확인)
- `--check`: exit code로만 상태 반환 (0=최신, 1=업데이트 있음, 2=오류)

동기화 대상은 `SKILL.md`, `references/`, `scripts/`, `assets/` 만이며, 그 외 로컬 파일(예: 작업 중인 임시 데이터)은 유지된다. 완료 후 `.commit-hash`가 새 SHA로 갱신된다.

**체크 실패 시**: 네트워크 오류 등으로 실패해도 워크플로우는 이전 버전으로 계속 진행한다. 단, 로그에 "스킬 업데이트 확인 실패"를 기록하고 사용자에게 공지한다.

## Phase −2: unity-cli 설치 보장 (스킬 실행 시 Phase −1 직후 수행)

이 스킬은 [geuneda/unity-cli](https://github.com/geuneda/unity-cli) 가 PATH 에 있어야 동작한다. **Phase 0 진입 전에 반드시** 아래 스크립트로 설치 여부를 확인하고, 없으면 자동으로 설치한다.

```bash
# 스킬 루트에서
bash scripts/ensure_unity_cli.sh
```

동작:

- PATH 에 `unity-cli` 가 있으면 그대로 종료 (재설치하지 않음)
- 없으면 `https://github.com/geuneda/unity-cli` 를 `~/.local/share/unity-cli` 로 클론 → `dotnet publish` (self-contained, 단일 파일) → `~/.local/bin/unity-cli` 심볼릭 링크 생성

옵션:

- `--check` : 설치 여부만 확인 (exit 0=있음, 1=없음, 2=오류). 워크플로우에서 사전 점검용.
- `--force` : 이미 설치되어 있어도 다시 빌드.
- `--update` : 레포를 `origin/main` 최신으로 fast-forward 한 뒤 재빌드. 새 명령(`ui.panel.create` 등) 누락 시 사용.

환경변수:

- `UNITY_CLI_INSTALL_DIR` (기본 `~/.local/share/unity-cli`) : 클론/빌드 디렉토리
- `UNITY_CLI_BIN_DIR` (기본 `~/.local/bin`) : 심볼릭 링크 위치

**필수 사전조건**: `git`, `dotnet` (net10.0+ SDK). `dotnet` 이 없으면 스크립트가 설치 안내 후 즉시 종료한다.

**`~/.local/bin` 이 PATH 에 없을 때**: 스크립트가 경고를 출력한다. 사용자에게 다음 줄을 셸 설정에 추가하도록 안내:

```bash
export PATH="$HOME/.local/bin:$PATH"
```

**설치 실패 시**: 빌드 로그를 출력하고 exit 2. 워크플로우는 중단하고 사용자에게 수동 설치를 안내한다 (`dotnet build src/UnityCli/UnityCli.csproj` 후 `dotnet src/UnityCli/bin/Debug/net10.0/UnityCli.dll <command>` 형태로 임시 사용 가능).

## Phase 0: Pre-flight 체크 (필수 - 실전에서 반드시 먼저 수행)

브릿지 버전마다 지원 명령이 다르다. 시작 전 **지원 명령 확인**하고 누락된 기능은 Editor 스크립트로 대체.

```bash
unity-cli status
unity-cli tool list | grep -E "(ui\.|component|gameobject|menu|editor|package)"
```

**검증된 명령 형식 (실측, 2026-04-21)**:

| 영역 | 검증된 호출 | 비고 |
|------|------------|------|
| 상태 | `unity-cli status` | bridge readiness 확인 |
| 패키지 | `unity-cli --json package list` / `package add ...` | 응답: `result.packages[]` |
| 컴파일 트리거 | `unity-cli menu execute path="Assets/Refresh"` | `editor refresh` 명령 **없음** |
| 메뉴 호출 | `unity-cli menu execute path="..."` | `result.result: bool` 반환 |
| 콘솔 조회 | `unity-cli --json console get` | 응답: `result.logs[].{message,timestamp}`. **logType/stackTrace 없음** → 문자열 패턴 매칭으로 분류 |
| 콘솔 비우기 | `unity-cli console clear` | sync 전후 노이즈 제거 |
| 게임오브젝트 조회 | `unity-cli --json gameobject get name="X"` | `path=` 가 아니라 `name=` 또는 `id=` |
| UI 스크린샷 | `unity-cli ui screenshot.capture outputPath=/abs/path.png` | **점 형태 액션 + `outputPath`** (`path=` 아님). size 미지정 시 1920x1080 |

**환경변수 → Editor 전달은 불가능**:
unity-cli 는 외부 bridge 통신이라 셸의 `export FIGMA_PAT=...` 가 Unity 프로세스에 전달되지 **않는다**. 자동화에서는 반드시 다음 방법을 쓴다:
- 파일 기반 컨텍스트: `{PROJECT}/Library/UguiFigmaContext.json` (run_unity_to_figma_sync.sh 가 자동 작성)
- 또는 EditorPrefs / PlayerPrefs (사람이 한 번 박아둠)

**빠지면 안 되는 사전 준비**:
1. 현재 씬 확인 (`unity-cli scene info`) → **BootScene 등 중요 씬 건드리지 말 것**. UnityToFigma 임포트 시 새 씬 생성 권장.
2. 기존 UI 계층 조회: 사용자에게 확인 또는 `gameobject get name="Canvas"` 로 spot-check.
3. Figma URL 입력 시: **UnityToFigma 패키지 설치 보장**
   ```bash
   bash scripts/ensure_unity_to_figma_package.sh
   ```
   `--check` 옵션은 상태만 확인. 자세한 동작은 `references/unity-to-figma-workflow.md` 참조.
4. TMP Essentials 확인. 없으면 부트스트랩이 `TMPro.TMP_PackageResourceImporter.ImportResources` 로 자동 임포트 시도 (다이얼로그 회피). 그래도 실패하면 사용자에게 `Window > TextMeshPro > Import TMP Essential Resources` 요청.
5. Editor 가 Play Mode 가 아닌지 확인 (`scene info` 의 `isLoaded` / 사용자 확인).
6. 스킬의 `scripts/ui_helper.sh`를 `/tmp/`로 복사 (보정 단계에서 사용).

**상세 내용**: `references/unity-cli-gotchas.md` 참조 (모든 시행착오 정리).

## 입력 소스 판별

사용자가 제공하는 입력에 따라 두 가지 경로로 분기한다:

| 입력 | 경로 | 우선도 |
|------|------|--------|
| Figma URL (`figma.com/design/...`) | **Path A: UnityToFigma 일괄 임포트 → 보정** | 우선 |
| 스크린샷 파일 경로 (`*.png`, `*.jpg`) | **Path B: 비전 분석 → unity-cli 구성** | 폴백 |

두 경로 모두 Phase 3 (검증 루프) 이후는 동일한 워크플로우를 따른다.

---

## Phase 1: Setup & Design Acquisition

### Path A: Figma URL 입력 (UnityToFigma 일괄 임포트 + 보정) — 권장

이 경로의 핵심은 **에이전트가 노드를 하나씩 분석하지 않고 UnityToFigma 패키지에 한 번에 맡기는 것**이다.
이전 버전처럼 MCP `get_design_context` 결과를 보고 `ui.*` 명령을 노드별로 호출하지 말 것 (비율 깨짐의 주범).

자세한 흐름·한계·트러블슈팅: `references/unity-to-figma-workflow.md`.

#### Step 1A.1: 사용자 입력 수집

다음 정보를 사용자로부터 받는다 (없으면 1회 질의):

| 항목 | 필수 | 비고 |
|------|------|------|
| Figma 문서 URL | O | 페이지/노드가 아닌 **문서 URL** (UnityToFigma 는 fileId 기준으로 전체 문서를 가져옴) |
| Figma Personal Access Token | O | https://www.figma.com/developers/api#authentication. PlayerPrefs 에 1회 저장 후 재사용 |
| 프로젝트 절대 경로 | O | unity-cli 가 붙어있는 Unity 프로젝트 루트 |
| 프로토타입 플로우 빌드 여부 | X | 기본 false. 화면 전환 자동 생성이 필요할 때만 true |
| 가져올 페이지 | X | 기본 전체. 일부만 원하면 사용자에게 별도 확인 후 처리 |

#### Step 1A.2: 환경 보장

```bash
# 1) unity-cli + bridge
unity-cli status

# 2) UnityToFigma 패키지 설치 보장 (없으면 git URL 추가, compile 까지)
bash scripts/ensure_unity_to_figma_package.sh

# 3) TMP Essentials 확인
unity-cli resource get packages/list | grep -i textmeshpro
# Essentials 가 없으면 사용자에게 안내하고 중단
```

#### Step 1A.3: 일괄 임포트 실행

부트스트랩 스크립트가 다음을 자동 처리한다:
- Editor 폴더에 `UnityToFigmaBootstrap.cs` 복사 → compile
- 설정 에셋 (`Assets/UnityToFigmaSettings.asset`) 자동 생성 + URL 주입
- PAT 을 `PlayerPrefs("FIGMA_PERSONAL_ACCESS_TOKEN")` 에 저장
- `BuildPrototypeFlow=false` 강제 (다이얼로그 회피)
- `UnityToFigma/Sync Document` 메뉴 실행

```bash
PROJECT_PATH=/abs/path/to/UnityProject \
FIGMA_DOCUMENT_URL='https://www.figma.com/design/.../...' \
FIGMA_PAT='figd_xxx' \
bash scripts/run_unity_to_figma_sync.sh
```

옵션:
- `UGUI_FIGMA_BUILD_PROTOTYPE_FLOW=true` : PrototypeFlow 빌드 활성화 (씬 다이얼로그 발생 가능 → 권장하지 않음)
- `UGUI_FIGMA_REPORT_PATH=...` : 리포트 출력 경로 변경 (기본 `Assets/_Temp/UnityToFigmaReport.json`)

#### Step 1A.4: 임포트 결과 검증

`run_unity_to_figma_sync.sh` 가 폴링까지 완료하므로 보통 추가 작업 불필요. 별도로 점검하려면:

```bash
unity-cli --json console get | python3 -c "
import sys, json
d = json.load(sys.stdin)
for m in d.get('result', {}).get('logs', []):
    msg = m.get('message') or ''
    if 'UnityToFigma import:' in msg:
        print(msg)
"
# 기대 결과: UnityToFigma import: created=N, updated=M, skipped=K, failed=0, orphaned=0, manifestRemoved=0
```

판정 기준:

| 결과 | 판정 |
|------|------|
| `failed > 0` | 임포트 부분 실패. `[UnityToFigma]` 메시지로 원인 파악 후 사용자에게 PAT/네트워크/문서 권한 확인 요청. 보정 단계 진입 금지. |
| `created + updated == 0` | 임포트 결과 없음. 페이지 선택 / URL 정확성 재확인. |
| 정상 | 다음 단계 진행 |

리포트 dump 결과 (`Assets/_Temp/UnityToFigmaReport.json`) 에서 다음을 확인:
- `importRoot` : 실제 임포트 루트 (settings.ImportRoot 기준, 기본 `Assets/Figma`)
- `screens` : `{importRoot}/Screens/*.prefab` 목록
- `components` : `{importRoot}/Components/*.prefab`
- `textures`, `serverImages`, `fonts`
- `roundedHandled` : `FigmaImage.cornerRadius != 0` 인 정상 라운드 노드 (UnityToFigma SDF 처리, 추가 작업 불필요)
- `roundedExtreme` : `max(cornerRadius) >= 500` (pill / circle 후보, 시각 검토 권장)
- `roundedSkipped` : 검출 실패 (대개 0; FigmaImage 타입 미참조 등)

#### Step 1A.5: 기본 화면 인스턴스화

`BuildPrototypeFlow=false` 인 경우 씬에 자동 인스턴스화되지 않는다. 본 스킬은 다음 메뉴로 첫 Screen 을 현재 씬에 띄운다:

```bash
unity-cli menu execute path="Tools/UnityToFigma Bootstrap/Instantiate Default Screen"
```

여러 화면을 띄워야 하면 사용자에게 어떤 Screen 프리팹을 띄울지 확인 후 직접 인스턴스화:

```bash
unity-cli asset add-to-scene path=Assets/Figma/Screens/MainScreen.prefab parentName=Canvas
```

> **참고**: 기본 Canvas 이름은 `Canvas` 가 아니라 `UICanvas` 가 자동 생성된다 (UnityToFigma + 부트스트랩 동작). `parentName=` 지정 시 이를 고려.

#### Step 1A.6: 레퍼런스 스크린샷 (선택, 검증용)

Figma MCP 가 연결돼 있으면 보정/검증용 스크린샷만 별도로 가져온다 (디자인 컨텍스트는 가져오지 않음 — 이 경로에선 불필요):

```
get_screenshot(fileKey=":fileKey", nodeId=":nodeId")
```

여러 화면이라면 Screen 프리팹 이름과 Figma Frame 이름을 매칭해서 1:1 비교 가능하도록 정리한다.

> 이 경로에선 `get_design_context` / `get_metadata` / 에셋 다운로드를 **수행하지 않는다**. UnityToFigma 가 모두 처리했다.

### Path B: 스크린샷 파일 입력 (폴백)

#### Step 1B.1: 스크린샷 수신

사용자로부터 레퍼런스 스크린샷 파일 경로를 받는다.
Read 도구로 이미지를 로드하여 비전 분석을 수행한다.

#### Step 1B.2: 정보 수집

다음 정보를 확인한다. 사용자가 제공하지 않은 항목은 질문한다:

| 항목 | 필수 | 기본값 | 예시 |
|------|------|--------|------|
| 레퍼런스 스크린샷 | O | - | /path/to/screenshot.png |
| 기준 해상도 | O | - | 1440x3040 |
| ScreenMatchMode | X | Expand | Expand, Shrink, MatchWidthOrHeight |
| matchWidthOrHeight | X | 0.5 | 0~1 사이 값 |

#### Step 1B.3: Canvas 생성

```bash
unity-cli ui canvas.create name=UICanvas \
  referenceResolution={width},{height} \
  screenMatchMode={mode}
```

---

## Phase 2A: Patch (Path A 전용 — UnityToFigma 결과 보정)

이 단계는 **UnityToFigma 가 만들어 둔 결과 위에 차이만 덧칠하는 단계**다. 전체 재구성 금지.

### Step 2A.1: 비교 캡처

레퍼런스(Figma 스크린샷)와 Game View 캡처를 동시에 로드해 비교한다. 캡처 도구가 없으면 `assets/GameViewCapture.cs` 사용 (Phase 3 와 동일 절차).

### Step 2A.2: 차이 분류

발견된 차이를 다음 카테고리로 분류한다:

| 카테고리 | 보정 행동 |
|---------|-----------|
| 폰트 매칭 실패 (다른 폰트로 대체됨) | 프로젝트 폰트로 교체 (`component update type=TMPro.TextMeshProUGUI values={"font": ...}`) |
| 색상 mismatch | `component update type=UnityEngine.UI.Image values={"color": "#..."}` |
| 텍스트 누락/오타 | `component update type=TMPro.TextMeshProUGUI values={"text": "..."}` (쉘 이스케이프 주의 — `scripts/batch_set_texts.py` 사용 권장) |
| Image Sprite 누락 (다운로드 실패) | 사용자에게 보고. 임의 placeholder 금지. |
| SafeArea 누락 | 화면 루트에 SafeArea 컴포넌트 수동 추가 |
| 단순 텍스트 정렬 차이 | TMP `alignment` 속성만 수정 |
| **라운드 코너 차이** | **수정하지 않음.** Path A 라면 UnityToFigma SDF 가 정확하므로 캡처 차이는 노이즈일 가능성 높음. `roundedHandled`/`roundedExtreme` 으로 이미 분류됨 (`references/round-corner-policy.md`) |
| RectTransform 위치/크기 차이 | **건드리지 않음 (기본).** UnityToFigma 가 constraints 로 잡은 값을 임의로 옮기면 다해상도에서 깨진다. 정말로 누락된 경우에만 보정. |

### Step 2A.3: 보정 명령 실행

색상 한 줄 패치 예:

```bash
unity-cli component update name=PrimaryButton type=UnityEngine.UI.Image values='{"color":"#1A1A2EFF"}'
```

폰트 일괄 교체:

```bash
# 1) 모든 TMP 의 font 를 프로젝트 표준으로 변경하는 Editor 메뉴 사용 (assets/FixTMPSettings.cs 와 같은 패턴)
cp ~/.claude/skills/ugui-from-screenshot/assets/FixTMPSettings.cs Assets/Editor/
unity-cli menu execute path="Assets/Refresh"   # 컴파일 트리거 (editor refresh 명령은 없음)
sleep 5                                          # 컴파일 안정화
unity-cli menu execute path="Tools/Fix TMP Overflow Settings"
```

### Step 2A.4: 보정 종료 조건

- 분류 결과 모든 항목이 "수동 처리" / "보정 완료" 상태가 되면 Phase 3 (검증 루프) 진입
- 보정 시도 횟수 누적 5회 초과 시 강제 중단하고 사용자 보고. UnityToFigma 가 처리하지 못한 영역을 무리하게 시도하지 말 것.

### 금지 사항 (재확인)

- 라운드 코너를 sprite/mask 로 흉내내지 않는다. `references/round-corner-policy.md` 참조.
- UnityToFigma 가 잡은 RectTransform 의 anchor/offset 을 일괄 갈아엎지 않는다.
- 자동 임포트된 SDF 도형(Rectangle/Ellipse)의 형상을 unity-cli 로 강제 변경하지 않는다 (인스펙터에서 사용자가 직접 조정).

---

## Phase 2: Analysis & Build (Path B 전용)

### Step 2.1: UI 구조 분석

**Figma 경로**: 메타데이터의 노드 트리와 디자인 컨텍스트에서 정확한 값을 추출.
**스크린샷 경로**: Claude 비전으로 스크린샷을 상세 분석.

분석 항목:
1. **UI 요소 식별**: 각 요소의 타입, 위치, 크기, 색상, 텍스트
2. **계층 구조 파악**: 헤더/콘텐츠/하단바 등 영역 구분
3. **반응형 앵커 결정**: Step 2.2의 알고리즘으로 결정 (절대좌표 금지)
4. **레이아웃 패턴**: 반복 요소 → Layout Group, 고정 바 → 스트레치 앵커

### Step 2.2: 반응형 앵커 결정 알고리즘 (핵심)

**다해상도 대응의 핵심은 모든 요소에 올바른 앵커를 할당하는 것이다.**
모든 요소를 center anchor + 절대 좌표로 배치하면 기준 해상도에서만 동작하므로 반드시 아래 알고리즘을 따른다.

#### 1단계: 요소의 역할 판별

| 디자인 역할 | 앵커 프리셋 | 좌표 방식 |
|-------------|------------|-----------|
| 전체 배경/오버레이 | `anchorMin=0,0 anchorMax=1,1` (풀 스트레치) | `offsetMin/offsetMax=0,0` |
| 상단 바/헤더 | `anchorMin=0,1 anchorMax=1,1 pivot=0.5,1` | `size=0,{height}` (가로 스트레치) |
| 하단 바/풋터 | `anchorMin=0,0 anchorMax=1,0 pivot=0.5,0` | `size=0,{height}` (가로 스트레치) |
| 콘텐츠 영역 (헤더/풋터 사이) | `anchorMin=0,0 anchorMax=1,1` | `offsetMin=0,{footerH} offsetMax=0,-{headerH}` |
| 중앙 카드/다이얼로그 | `anchorMin=0.5,0.5 anchorMax=0.5,0.5` | 고정 `size={w},{h}` |
| 좌측 고정 사이드바 | `anchorMin=0,0 anchorMax=0,1 pivot=0,0.5` | `size={w},0` |
| 우측 고정 사이드바 | `anchorMin=1,0 anchorMax=1,1 pivot=1,0.5` | `size={w},0` |

#### 2단계: Figma 레이아웃 속성에서 앵커 추론

`get_design_context` 반환값(Tailwind CSS 클래스)에서 레이아웃 의도를 분석:

| Tailwind/CSS 패턴 | UGUI 앵커 결정 |
|-------------------|---------------|
| `w-full` 또는 `justify-self-stretch` | 가로 스트레치 (`anchorMin.x=0, anchorMax.x=1`) |
| `h-full` 또는 `self-stretch` | 세로 스트레치 (`anchorMin.y=0, anchorMax.y=1`) |
| `flex-[1_0_0]` (flex-grow) | 부모 내 비율 분배 → Layout Group + 스트레치 |
| `absolute top-0 left-0` | 부모 좌상단 앵커 |
| `flex flex-col` 또는 `flex-col` | Vertical Layout Group |
| `flex` (기본 수평) | Horizontal Layout Group |
| `grid grid-cols-[...]` | 비율 기반 앵커 분할 또는 Grid Layout |
| `size-full` | 풀 스트레치 |
| `shrink-0` + 고정 w/h | 고정 크기, center 또는 가장자리 앵커 |

#### 3단계: 부모-자식 관계에 따른 좌표 계산

앵커 타입별 좌표 변환 공식 (`references/figma-to-ugui-mapping.md` 상세 참조):

**스트레치 앵커 (가로)**: `anchorMin.x=0, anchorMax.x=1`
```
offsetMin.x = figma.x                        (왼쪽 여백)
offsetMax.x = -(parentW - figma.x - figma.w)  (오른쪽 여백, 음수)
sizeDelta.x = 0                               (앵커가 결정)
```

**스트레치 앵커 (세로)**: `anchorMin.y=0, anchorMax.y=1`
```
offsetMin.y = parentH - figma.y - figma.h     (하단 여백)
offsetMax.y = -figma.y                         (상단 여백, 음수)
sizeDelta.y = 0
```

**고정 앵커 (center)**: `anchorMin=0.5,0.5 anchorMax=0.5,0.5`
```
anchoredPosition.x = figma.x + figma.w/2 - parentW/2
anchoredPosition.y = -(figma.y + figma.h/2 - parentH/2)
sizeDelta = figma.w, figma.h
```

**상단 고정 (top stretch)**: `anchorMin=0,1 anchorMax=1,1 pivot=0.5,1`
```
anchoredPosition = 0, 0
sizeDelta = 0, figma.h      (가로는 스트레치, 세로는 고정)
offsetMin.x = figma.x       (왼쪽 마진이 있으면)
offsetMax.x = -(parentW - figma.x - figma.w) (오른쪽 마진이 있으면)
```

#### 4단계: 2-column 이상 레이아웃 처리

Figma에서 수평으로 나란한 프레임은 비율 앵커 또는 Layout Group으로 변환:

**방법 A: 비율 앵커 (반응형)**
```
// 2-column: 왼쪽 56.8%, 오른쪽 43.2% (예: 521.5/918 vs 372.5/918)
LeftCol:  anchorMin=0,0 anchorMax=0.568,1
RightCol: anchorMin=0.594,0 anchorMax=1,1   (gap 비율 반영)
```

**방법 B: Horizontal Layout Group (균등 또는 자동)**
```bash
unity-cli ui layout.add name=ContentSection layoutType=Horizontal \
  spacing=24 childAlignment=MiddleCenter \
  childForceExpandWidth=true childForceExpandHeight=true
```

### Step 2.3: Figma 값 → UGUI 매핑

| Figma 속성 | UGUI 매핑 |
|------------|-----------|
| Frame position (x, y) | 앵커 타입에 따라 다름 (Step 2.2 참조) |
| Frame size (w, h) | 스트레치면 `offsetMin/Max`, 고정이면 `size` |
| Fill color | `color` (hex) |
| Text content | `text` |
| Font size | `fontSize` |
| Font weight (Bold/Regular) | `fontStyle` |
| Text alignment | `alignment` |
| Auto Layout (horizontal) | `ui layout.add layoutType=Horizontal` |
| Auto Layout (vertical) | `ui layout.add layoutType=Vertical` |
| Auto Layout spacing | `spacing` |
| Auto Layout padding | `paddingLeft/Right/Top/Bottom` |
| `w-full` / flex stretch | 가로 스트레치 앵커 |
| `h-full` / self-stretch | 세로 스트레치 앵커 |
| `absolute` + top/left | 해당 가장자리 앵커 |
| `grid grid-cols-[비율]` | 비율 앵커 분할 |

### Step 2.4: UI 빌드

부모 → 자식 순서로 생성. **모든 요소에 적절한 앵커를 사용한다.**

#### 2.4.a: 명령이 모두 지원되는 최신 브릿지

```bash
unity-cli ui panel.create canvasName=UICanvas name=Header parentName=Card \
  anchorMin=0,1 anchorMax=1,1 pivot=0.5,1 \
  anchoredPosition=0,0 size=0,{headerHeight} color={headerColor}
```

#### 2.4.b: 구버전 브릿지 (panel.create / recttransform.modify / layout.add 없음)

**실전에서 자주 부딪히는 경우. 아래 3단계 패턴을 모든 UI 요소에 적용한다.**

**부모 지정 3단계 (필수)**: `ui.*.create`의 `parentName`/`parentId`는 조용히 무시되어 항상 Canvas 아래에 생성된다. 또한 `gameobject reparent`는 RectTransform을 망가뜨린다. 따라서:

1. 생성 (Canvas 아래로 감)
2. `gameobject reparent name=X parentId=<부모_id> worldPositionStays=false`
3. `component update name=X type=UnityEngine.RectTransform values=...`로 rect 재설정

**헬퍼 사용**: 스킬의 `scripts/ui_helper.sh`를 `/tmp/`로 복사해 사용. 한 번에 3단계를 처리하고 새 InstanceID를 반환한다.

```bash
# 헬퍼 준비 (작업 시작 시 1회)
cp "$(dirname $(which unity-cli))/../skills/ugui-from-screenshot/scripts/ui_helper.sh" /tmp/ui_helper.sh
chmod +x /tmp/ui_helper.sh

# Canvas 생성 (최상위, reparent 불필요)
unity-cli ui canvas.create name=UICanvas referenceResolution=1440,3040 screenMatchMode=Expand
# -> result.id 를 CANVAS_ID 에 저장 (예: -37324)

# MainCard: Canvas 자식으로 중앙 배치 (984x666 center)
MAINCARD_ID=$(/tmp/ui_helper.sh create_ui image MainCard $CANVAS_ID 0.5 0.5 0.5 0.5 0.5 0.5 0 0 984 666 color=#FFFFFFEB)

# Header: MainCard의 좌상단 기준 (32, -32) 위치
HEADER_ID=$(/tmp/ui_helper.sh create_ui image Header $MAINCARD_ID 0 1 0 1 0 1 32 -32 918 77 color=#00000000)
```

**반응형 앵커 공식**은 Step 2.2에 정리되어 있으나, 실전 경험상 **카드형 고정 UI**는 단순 매핑이 훨씬 빠르다:
- 자식 요소: `anchor=(0,1)`, `pivot=(0,1)` (부모 좌상단)
- `anchoredPosition = (figma.x, -figma.y)` (Y 부호 반전)
- `sizeDelta = (figma.w, figma.h)`

반응형이 진짜 필요할 때만 Step 2.2/2.3의 스트레치 앵커 전략을 개별 적용.

#### 2.4.c: 텍스트 내용 설정 (쉘 이스케이프 주의)

**버그**: `unity-cli ui text.create text="MCP Import Smoke"`는 bash 분리로 "MCP"만 전달됨.

**해결**: 생성 시엔 임시값("X")로 만들고, 완료 후 Python에서 JSON으로 일괄 업데이트.
- 템플릿: `scripts/batch_set_texts.py`
- 속성명은 `text` (NOT `m_text`)
- 검증: `unity-cli resource get ui/hierarchy` 로 실제 반영 확인

#### 2.4.d: TMP 오버플로우 처리

TMP 기본 `overflowMode`는 `Ellipsis`(1)이라 긴 텍스트가 "..."로 잘린다. 반드시:
- `textWrappingMode = Normal` (1)
- `overflowMode = Overflow` (0)

**Editor 스크립트로 일괄 처리 권장** (JSON 설정만으론 mesh 리빌드 누락 케이스 존재):
- 템플릿: `assets/FixTMPSettings.cs` → 프로젝트 `Assets/_Project/Scripts/Editor/`에 복사
- 실행: `unity-cli menu execute path="Tools/Fix TMP Overflow Settings"`

#### 2.4.e: 기본 UISprite를 평면 스프라이트로 교체

`ui.image.create`로 만든 Image는 sprite=null이지만 Unity가 둥근 UISprite로 렌더할 수 있다. 각진 사각형을 원하면 프로젝트의 1x1 화이트 스프라이트로 교체:
- 템플릿: `assets/ReplaceUISprites.cs` → `TargetSpritePath` 프로젝트에 맞게 수정 후 복사
- 실행: `unity-cli menu execute path="Tools/Replace UI Images With Flat Sprite"`

### Step 2.5: 리소스 매칭

**Figma 경로**: MCP가 반환한 에셋 URL에서 다운로드하여 spritePath로 할당.
**스크린샷 경로**: 프로젝트 Assets에서 유사 스프라이트 검색 (Glob 도구). 없으면 placeholder.

---

## Phase 3: Verification Loop

최대 5회 반복. 90%+ 일치 시 PASS.

### Step 3.1: 캡처

**구버전 브릿지**: `ui.screenshot.capture` / `editor.gameview.resize`가 없다. 스킬의 `assets/GameViewCapture.cs`를 프로젝트 `Assets/_Project/Scripts/Editor/`에 복사한 뒤 메뉴 실행:

```bash
# 1회 준비: 스크립트 복사 후 컴파일 (Assets/Refresh 메뉴로 트리거 — editor compile 명령은 없음)
cp ~/.claude/skills/ugui-from-screenshot/assets/GameViewCapture.cs Assets/Editor/
unity-cli menu execute path="Assets/Refresh" && sleep 5

# 캡처 (전체 Canvas)
unity-cli menu execute path="Tools/Capture Game View"
# → Assets/_Temp/GameCapture.png (1440x3040)

# 카드만 (비교 시 편리)
unity-cli menu execute path="Tools/Capture Card Only"
# → Assets/_Temp/CardCapture.png (984x666)
```

**캡처 주의**:
- Overlay Canvas는 카메라로 렌더 안 됨 → 스크립트 내부에서 ScreenSpaceCamera로 **임시 전환 후 원복**
- CanvasScaler가 ScaleWithScreenSize면 Screen.width/height와 RT 불일치로 축소됨 → 스크립트 내부에서 ConstantPixelSize(1.0)로 **임시 전환 후 원복**
- GameView RT 직접 캡처 시엔 **상하 반전** 발생 → 스크립트에서 플립 필요
- 캡처 실패 시 씬이 임시 상태로 저장되면 안 됨 → 스크립트에서 반드시 원복 보장

### Step 3.2: 비교

Read 도구로 레퍼런스(Figma 스크린샷 또는 원본 파일)와 캡처 스크린샷을 동시에 로드하여 비교.

평가 기준 (가중치):

| Category | Weight | Pass Criteria |
|----------|--------|---------------|
| STRUCTURE | 30% | 모든 요소 존재 및 올바른 계층 |
| POSITION | 25% | 기준 해상도 대비 5% 이내 오차 |
| SIZE | 20% | 기준 크기 대비 10% 이내 오차 |
| COLOR | 15% | 색상 hex 값 근사 일치 |
| TEXT | 10% | 텍스트 내용 완전 일치 |

### Step 3.3: 수정

비교 결과를 기반으로 수정 명령 실행. **수정 행동은 입력 경로에 따라 다르다**:

**Path A (UnityToFigma) 의 경우**:
- Phase 2A 의 보정 행동 표를 따른다.
- RectTransform 일괄 변경 금지. 색상/텍스트/폰트 위주.
- 라운드 차이는 무시하고 보고 항목으로 누적.

**Path B (스크린샷) 의 경우**:
- 자유롭게 RectTransform/Layout 조정 가능.

```bash
# Path B 예시 (Path A 에선 RectTransform 일괄 변경 자제)
unity-cli ui recttransform.modify name=Header anchoredPosition=0,-10 size=0,200
unity-cli component update name=Header type=UnityEngine.UI.Image \
  values={"color":"#1A1A2EFF"}
unity-cli component update name=HeaderTitle type=TMPro.TextMeshProUGUI \
  values={"fontSize":52,"text":"Corrected Title"}
unity-cli ui layout.add name=Content layoutType=Vertical spacing=20
```

### Step 3.4: 반복 판정

- 90%+ → PASS, Phase 4로 진행
- 5회 초과 → 현재 상태 보고, 남은 이슈 목록화, 사용자 판단 요청
- **라운드 코너로 인한 mismatch 는 점수에서 제외**한다 (자동 처리 정책상 보정 불가). `roundedSkipped` 항목에 기록만 한다.

## Phase 4: Multi-Resolution Validation

`references/resolution-profiles.md` 참조.

최대 3회/해상도 반복.

### Step 4.1: 해상도별 캡처

```bash
# editor.gameview.resize 명령은 없음 → assets/GameViewCapture.cs 의 Resize 메뉴 또는 RT 크기 지정으로 처리
unity-cli menu execute path="Tools/Capture Game View Resize {w}x{h}"   # 사전에 등록된 메뉴
unity-cli ui screenshot.capture outputPath=/abs/path/Screenshots/resolution_{device}_{n}.png
# 또는 직접 캡처 스크립트가 RT (w,h) 로 렌더 후 저장
```

### Step 4.2: 검증 기준

| Check | Pass Criteria |
|-------|---------------|
| Clipping | 요소가 화면 밖으로 잘리지 않음 |
| Overlap | 요소 간 비정상적 겹침 없음 |
| Readability | 텍스트 가독성 유지 |
| Proportion | 비율 15% 이내 유지 |
| Spacing | 여백/간격 비정상적 확대/축소 없음 |

### Step 4.3: 반응형 수정

문제 패턴별 해결:

| 문제 | 해결 |
|------|------|
| 가로 잘림 | anchorMin.x=0, anchorMax.x=1 로 스트레치 |
| 세로 비율 깨짐 | Layout + ContentSizeFitter 조합 |
| 텍스트 잘림 | overflow=Ellipsis 또는 fontSize 축소 |
| 과도한 여백 | maxWidth 제한 또는 비율 앵커 |
| 요소 겹침 | Layout spacing 조정 |

수정 후 모든 해상도에서 재검증.

### Step 4.4: 반복 판정

- 모든 해상도 통과 → Phase 5
- 3회 초과 미통과 → 해당 해상도 이슈 보고, 사용자 판단 요청

## Phase 5: Complete

1. Game View 기준 해상도 복원: `editor gameview.resize` 명령은 unity-cli 에 없으므로 `assets/GameViewCapture.cs` 의 RT 크기 지정으로 처리하거나 사용자에게 Game View 종횡비 변경 요청.

2. 결과 보고 (입력 경로에 따라 항목 다름):

**Path A (UnityToFigma) 보고서 양식**:
```
## UGUI 구성 완료 (UnityToFigma 경로)

### 입력: Figma 문서 ({URL})
### 임포트 요약: created=N, updated=N, skipped=N, failed=N, orphaned=N
### 최종 일치율: {score}%  (라운드 차이는 점수에서 제외)

### 생성 산출물 (importRoot={importRoot})
Screens/    : N 프리팹
Components/ : N 프리팹
Pages/      : N 프리팹
Textures/   : N 이미지
ServerRenderedImages/ : N 벡터 PNG
Fonts/      : N 폰트 (Google Fonts 다운로드 포함)

### 보정 내역 (Phase 2A)
- Iteration 1: TitleText 폰트 매칭 실패 → ProjectStandard 로 교체
- Iteration 2: PrimaryButton 색상 #1A1A2EFF 로 보정
- ...

### 라운드 코너 처리 결과 (UnityToFigma SDF 자동 처리)
- 정상 처리(roundedHandled): N 개 → 추가 작업 불필요
- pill/circle 후보(roundedExtreme): M 개 → 디자인 의도 일치 여부만 시각 확인 권장
  - {prefab path / cornerRadius} ...
- 검출 실패(roundedSkipped): K 개 (대개 0)

### 사용자 후속 작업 안내
- 폰트 매칭 실패 항목: 프로젝트에 TTF 추가 후 동기화 재실행
- 임포트되지 않은 효과(Inner Shadow/Blur 등): 미지원이므로 별도 구현 필요
- (선택) PrototypeFlow 가 필요하면 UGUI_FIGMA_BUILD_PROTOTYPE_FLOW=true 로 재동기화
- (선택) ImportRoot 변경: Assets/UnityToFigmaSettings.asset 의 ImportRoot 필드
```

**Path B (스크린샷) 보고서 양식**:
```
## UGUI 구성 완료 (스크린샷 경로)

### 입력: {스크린샷 경로}
### 최종 일치율: {score}%  (라운드 차이는 제외)

### 해상도별 결과
| Device | Resolution | Result |
|--------|-----------|--------|
| ...    | ...       | PASS   |

### 생성된 UI 계층 구조
UICanvas
  Header
    ...

### 수정 내역
- Iteration 1: Header 높이 180->200, 색상 수정
- Iteration 2: Content spacing 16->20

### 라운드 처리 미수행 항목 (사용자 수동 작업 필요)
{각진 placeholder 로 둔 라운드 후보들}

### 임시 파일
Assets/Screenshots/verify_*.png -- 삭제 가능
Assets/Screenshots/resolution_*.png -- 삭제 가능
```

3. 추가 작업 안내:
   - 라운드 처리 (`references/round-corner-policy.md` 의 사용자 안내 문구 그대로 출력)
   - 커스텀 폰트 적용 필요 시 안내
   - SafeArea 대응 필요 시 안내

## Error Handling

| 상황 | 대응 |
|------|------|
| Bridge 연결 실패 | `unity-cli status`로 확인 안내, Editor 가 켜져 있는지 사용자에게 요청 |
| **Path A** UnityToFigma 패키지 설치 실패 | `package list` 결과 / Console 로그 확인. Unity 6 (6000.0+) 인지 확인. |
| **Path A** Sync 후 `failed > 0` | Console `[UnityToFigma]` 메시지 확인. 보정 단계 진입 금지. PAT/문서 권한 사용자 확인 요청. |
| **Path A** PAT 입력 다이얼로그 발생 | 부트스트랩이 PAT 을 PlayerPrefs 에 저장 못 함. `FIGMA_PAT` env 또는 EditorPrefs `ugui.figma.pat` 확인. |
| **Path A** TMP Essentials 미설치 | 사용자에게 `Window > TextMeshPro > Import TMP Essential Resources` 안내 후 중단. |
| **Path A** 라운드 mismatch 발견 | **수정하지 말 것.** `roundedSkipped` 항목으로 누적, Phase 5 보고. |
| **Path B** Figma MCP 미연결 | MCP 서버 활성화 안내, 스크린샷 경로로 폴백 제안 |
| **Path B** Figma URL 파싱 실패 | URL 형식 안내, 수동 fileKey/nodeId 입력 요청 |
| **Path B** get_design_context 응답 과대 | get_metadata 로 구조 파악 후 자식 노드별 개별 조회 |
| 에셋 다운로드 실패 | placeholder Image로 생성, 수동 교체 안내. **임의 sprite 강제 할당 금지.** |
| GameObject 이름 중복 | 고유 이름 사용 (기능적 명칭) |
| 캡처 실패 (Edit Mode) | Play Mode 폴백 시도 |
| 해상도 변경 실패 | 수동 Game View 설정 안내 |
| 반복 횟수 초과 | 현재 상태 + 이슈 보고, 사용자 결정 |

## References

- `./references/unity-to-figma-workflow.md` -- **Path A 필독**. UnityToFigma 일괄 임포트 흐름, 다이얼로그 우회, 한계, 보정 패턴
- `./references/round-corner-policy.md` -- **전역 필독**. 라운드 코너 자동 처리 금지 정책
- `./references/unity-cli-gotchas.md` -- 실제 브릿지 시행착오 및 우회 방법
- `./references/new-commands.md` -- 최신 unity-cli 명령어 레퍼런스
- `./references/anchoring-strategy.md` -- 반응형 앵커링 패턴 가이드 (주로 Path B 용)
- `./references/resolution-profiles.md` -- 다해상도 검증 프로파일
- `./references/figma-to-ugui-mapping.md` -- Figma 속성 → UGUI 매핑 상세 (Path B 보정 시 참조)

## Scripts

- `./scripts/ensure_unity_cli.sh` -- unity-cli 설치 보장 (Phase −2). PATH 검사 → 미설치 시 GitHub 클론 + `dotnet publish` + `~/.local/bin/unity-cli` 심볼릭 링크. `--check` / `--force` / `--update` 옵션.
- `./scripts/ensure_unity_to_figma_package.sh` -- UnityToFigma 패키지 설치 보장 (Phase 0, Path A 전용). `--check` / `--update` 옵션.
- `./scripts/run_unity_to_figma_sync.sh` -- 부트스트랩 복사 + 동기화 + 리포트 dump 의 일괄 실행. env: `PROJECT_PATH`, `FIGMA_DOCUMENT_URL`, `FIGMA_PAT`, `UGUI_FIGMA_BUILD_PROTOTYPE_FLOW`.
- `./scripts/check_update.sh` -- 스킬 자체 자동 업데이트 (Phase −1).
- `./scripts/ui_helper.sh` -- 생성+reparent+rect 재설정 3단계 통합 헬퍼 (Path B 구버전 브릿지용). `/tmp/`에 복사해 사용.
- `./scripts/batch_set_texts.py` -- TMP 텍스트 일괄 설정 템플릿 (쉘 이스케이프 회피).

## Assets

프로젝트 `Assets/_Project/Scripts/Editor/`에 복사해 사용:

- `./assets/UnityToFigmaBootstrap.cs` -- **Path A 핵심**. UnityToFigma 메뉴를 다이얼로그 없이 호출하기 위한 부트스트랩 + 결과 리포트 dump + Screen 인스턴스화 메뉴. `Tools/UnityToFigma Bootstrap/*` 메뉴 추가.
- `./assets/GameViewCapture.cs` -- `Tools/Capture Game View`, `Tools/Capture Card Only` 메뉴 제공.
- `./assets/FixTMPSettings.cs` -- 모든 TMP의 overflow/wrap 일괄 수정 (Ellipsis 방지).
- `./assets/ReplaceUISprites.cs` -- (Path B 전용) 모든 UI Image의 sprite를 평면 화이트로 일괄 교체. **Path A 결과에는 사용하지 말 것** (UnityToFigma 의 SDF/Sprite 결과를 망친다).
