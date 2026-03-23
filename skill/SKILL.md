---
name: ugui-from-screenshot
description: UI 디자인을 기반으로 unity-cli를 사용하여 UGUI를 자동 구성하는 스킬. Figma URL(MCP) 또는 스크린샷 파일을 입력으로 받아 Canvas 설정, UI 요소 생성, 반복 검증(90%+ 일치), 다해상도 대응 점검을 수행한다. 'UGUI from screenshot', 'UGUI from Figma', '스크린샷으로 UI', 'UI 구현', 'screenshot to UGUI', '스크린샷 기반 UI', 'Figma로 UGUI', 'Figma UI 구현' 등의 요청 시 트리거된다.
metadata:
  mcp-server: figma
---

# UGUI from Design

UI 디자인(Figma URL 또는 스크린샷)을 분석하여 unity-cli로 UGUI를 자동 구성하고, 반복 검증-수정 루프를 통해 90%+ 일치를 달성하는 스킬.

## Prerequisites

- unity-cli 설치 및 Bridge 연결 상태 (브릿지 개선 버전 필요)
- Unity Editor에 unity-connector 패키지 설치
- 브릿지 상태 확인: `unity-cli status`
- Figma URL 입력 시: Figma MCP 서버 연결 필요

## 입력 소스 판별

사용자가 제공하는 입력에 따라 두 가지 경로로 분기한다:

| 입력 | 경로 | 우선도 |
|------|------|--------|
| Figma URL (`figma.com/design/...`) | **Figma MCP 경로** (정밀한 구조/색상/크기 데이터) | 우선 |
| 스크린샷 파일 경로 (`*.png`, `*.jpg`) | **비전 분석 경로** (Claude 비전 기반) | 폴백 |

두 경로 모두 Phase 2 이후는 동일한 워크플로우를 따른다.

---

## Phase 1: Setup & Design Acquisition

### Path A: Figma URL 입력 (권장)

#### Step 1A.1: URL 파싱

Figma URL에서 fileKey와 nodeId를 추출한다.

URL 형식: `https://figma.com/design/:fileKey/:fileName?node-id=:nodeId`

예시:
- URL: `https://figma.com/design/4mTmCx1W8EgGGjJL3v2K0l/제목-없음?node-id=0-1`
- fileKey: `4mTmCx1W8EgGGjJL3v2K0l`
- nodeId: `0-1` (또는 `0:1`)

브랜치 URL인 경우: `https://figma.com/design/:fileKey/branch/:branchKey/:fileName` → branchKey를 fileKey로 사용.

#### Step 1A.2: 메타데이터 조회

`get_metadata`로 페이지 전체 구조를 파악한다:

```
get_metadata(fileKey=":fileKey", nodeId=":nodeId")
```

반환값: XML 형식의 노드 트리 (ID, 레이어 타입, 이름, 위치, 크기 포함).
이를 통해 Frame, Rectangle, Text, Image 등 UI 구조를 식별한다.

#### Step 1A.3: 디자인 컨텍스트 조회

주요 노드별로 `get_design_context`를 호출하여 상세 디자인 데이터를 가져온다:

```
get_design_context(fileKey=":fileKey", nodeId=":nodeId", clientLanguages="csharp", clientFrameworks="unity")
```

반환값:
- 레이아웃 속성 (Auto Layout, constraints, sizing)
- 타이포그래피 (font, size, weight, line-height)
- 색상 (hex, RGBA)
- 컴포넌트 구조 및 variant
- 간격/패딩 값
- **스크린샷** (기본 포함)
- **에셋 다운로드 URL** (아이콘, 이미지)

응답이 너무 큰 경우: metadata에서 자식 노드 ID를 확인하고 개별 호출.

#### Step 1A.4: 레퍼런스 스크린샷 획득

비교 검증용 스크린샷을 가져온다:

```
get_screenshot(fileKey=":fileKey", nodeId=":nodeId")
```

이 스크린샷이 Phase 3 검증 루프의 비교 기준이 된다.

#### Step 1A.5: 에셋 다운로드 및 Sprite 임포트

Figma MCP가 반환한 에셋 URL에서 아이콘/이미지를 다운로드하고 Unity Sprite로 임포트한다.

**0단계: 에셋 레지스트리 초기화**

다운로드할 에셋 목록을 먼저 수집하고, 중복을 제거한다.
내부적으로 `{asset_url → local_path}` 매핑을 유지한다.

```
assetRegistry = {}   # URL → Unity 에셋 경로 매핑
```

**1단계: 에셋 URL 추출 및 분류**

`get_design_context` 응답에서 이미지 상수를 식별하고 용도별로 분류한다:
```
const image = 'https://www.figma.com/api/mcp/asset/550e8400-...'
```

분류 기준 (Figma 노드의 크기/이름/용도로 판단):

| 분류 | 판단 기준 | 폴더 |
|------|-----------|------|
| Icons | 크기 ≤ 64px, 이름에 icon/ic/arrow/chevron 포함 | `Icons/` |
| Images | content 영역 내 이미지, 일러스트, 사진 | `Images/` |
| Backgrounds | 전체 화면/섹션 배경, 이름에 bg/background 포함 | `Backgrounds/` |
| Buttons | 버튼 내부 이미지, 이름에 btn 포함 | `Buttons/` |
| 기타 | 위에 해당하지 않는 에셋 | `Misc/` |

**2단계: 중복 확인 후 다운로드**

에셋마다 다운로드 전에 이미 존재하는지 확인한다:

```bash
# 폴더 생성 (최초 1회)
mkdir -p {projectPath}/Assets/FigmaAssets/{Icons,Images,Backgrounds,Buttons,Misc}

# 각 에셋에 대해:
# 1) 이미 다운로드된 에셋인지 확인
if [ ! -f {projectPath}/Assets/FigmaAssets/Icons/{name}.png ]; then
  curl -o {projectPath}/Assets/FigmaAssets/Icons/{name}.png {asset_url}
fi
```

같은 URL이 여러 UI 요소에서 참조되면 한 번만 다운로드하고 경로를 재사용한다:
```
# 레지스트리에 등록
assetRegistry[asset_url] = "Assets/FigmaAssets/Icons/{name}.png"

# 이후 같은 URL 참조 시 레지스트리에서 경로만 가져옴
spritePath = assetRegistry[asset_url]
```

**3단계: Unity에 에셋 등록**

모든 에셋을 다운로드한 후 한 번만 호출한다 (에셋마다 호출하지 않는다):
```bash
unity-cli editor refresh
```

**4단계: Sprite 임포트 설정 (필수!)**

Unity는 PNG/JPG를 기본적으로 `Default` 텍스처로 임포트한다. UGUI Image에 사용하려면 반드시 `Sprite` 타입으로 변환해야 한다.

새로 다운로드한 에셋만 임포트 설정을 변경한다 (이미 Sprite인 에셋은 건너뜀):

```bash
unity-cli asset import-texture path=Assets/FigmaAssets/Icons/icon_back.png textureType=Sprite
unity-cli asset import-texture path=Assets/FigmaAssets/Images/hero.png textureType=Sprite maxTextureSize=2048
```

`asset.import-texture`는 이미 올바른 설정이면 재임포트를 건너뛴다 (`reimported: false`).

추가 옵션:
- `spriteMode=1` (Single), `spriteMode=2` (Multiple) -- 기본값: Single
- `maxTextureSize=2048` -- 큰 원화의 경우
- `filterMode=Bilinear` / `Point` / `Trilinear`

**5단계: Image 생성 시 에셋 할당**
```bash
unity-cli ui image.create canvasName=UICanvas name=HeroImage \
  parentName=Content spritePath=Assets/FigmaAssets/Images/hero.png \
  preserveAspect=true size=400,300
```

- `preserveAspect=true`: 원본 비율 유지 (원화/아이콘에 필수)
- `useNativeSize=true`: 스프라이트 원본 해상도로 크기 설정
- 같은 스프라이트를 여러 UI 요소에서 사용할 때 동일한 `spritePath` 지정

#### Step 1A.6: 정보 추출 및 Canvas 설정

메타데이터에서 추출한 정보:

| 정보 | 추출 방법 |
|------|-----------|
| 기준 해상도 | 최상위 Frame 크기 (예: 1440x3040) |
| UI 계층 | 노드 트리 구조 |
| 각 요소 위치/크기 | 노드별 position, size |
| 색상 | fill/stroke 값 (hex) |
| 텍스트 | text content, fontSize, fontWeight |

사용자에게 ScreenMatchMode만 확인 후 Canvas 생성:

```bash
unity-cli ui canvas.create name=UICanvas \
  referenceResolution={frameWidth},{frameHeight} \
  screenMatchMode={mode}
```

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

## Phase 2: Analysis & Build

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

```bash
# 1. 상단 바 (가로 스트레치, 고정 높이)
unity-cli ui panel.create canvasName=UICanvas name=Header \
  anchorMin=0,1 anchorMax=1,1 pivot=0.5,1 \
  anchoredPosition=0,0 size=0,{headerHeight} color={headerColor}

# 2. 콘텐츠 영역 (상하 바 사이 스트레치)
unity-cli ui panel.create canvasName=UICanvas name=Content \
  anchorMin=0,0 anchorMax=1,1 pivot=0.5,0.5
unity-cli ui recttransform.modify name=Content \
  offsetMin=0,{bottomBarH} offsetMax=0,-{headerH}

# 3. 중앙 카드 (고정 크기, center anchor)
unity-cli ui panel.create canvasName=UICanvas name=Card \
  parentName=Content anchorMin=0.5,0.5 anchorMax=0.5,0.5 \
  anchoredPosition=0,0 size={w},{h} color={cardColor}

# 4. 카드 내부 2-column (비율 앵커)
unity-cli ui panel.create canvasName=UICanvas name=LeftCol \
  parentName=Card anchorMin=0,0 anchorMax=0.568,1 pivot=0.5,0.5
unity-cli ui recttransform.modify name=LeftCol offsetMin=24,24 offsetMax=-12,0

unity-cli ui panel.create canvasName=UICanvas name=RightCol \
  parentName=Card anchorMin=0.594,0 anchorMax=1,1 pivot=0.5,0.5
unity-cli ui recttransform.modify name=RightCol offsetMin=12,24 offsetMax=-24,0

# 5. 하단 바 (가로 스트레치, 고정 높이)
unity-cli ui panel.create canvasName=UICanvas name=Footer \
  anchorMin=0,0 anchorMax=1,0 pivot=0.5,0 \
  anchoredPosition=0,0 size=0,{footerH} color={footerColor}
```

### Step 2.5: 리소스 매칭

**Figma 경로**: MCP가 반환한 에셋 URL에서 다운로드하여 spritePath로 할당.
**스크린샷 경로**: 프로젝트 Assets에서 유사 스프라이트 검색 (Glob 도구). 없으면 placeholder.

---

## Phase 3: Verification Loop

최대 5회 반복. 90%+ 일치 시 PASS.

### Step 3.1: 캡처

```bash
unity-cli editor gameview.resize width={refWidth} height={refHeight}
unity-cli ui screenshot.capture width={refWidth} height={refHeight} \
  outputPath=Assets/Screenshots/verify_{n}.png
```

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

비교 결과를 기반으로 수정 명령 실행:

```bash
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

## Phase 4: Multi-Resolution Validation

`references/resolution-profiles.md` 참조.

최대 3회/해상도 반복.

### Step 4.1: 해상도별 캡처

```bash
unity-cli editor gameview.resize width={w} height={h}
unity-cli ui screenshot.capture width={w} height={h} \
  outputPath=Assets/Screenshots/resolution_{device}_{n}.png
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

1. Game View 기준 해상도 복원:
   ```bash
   unity-cli editor gameview.resize width={refWidth} height={refHeight}
   ```

2. 결과 보고:
   ```
   ## UGUI 구성 완료

   ### 입력 소스: Figma / 스크린샷
   ### 최종 일치율: {score}%

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
   - ...

   ### 다운로드된 Figma 에셋
   Assets/FigmaAssets/
     Icons/icon_back.png (2 곳에서 사용)
     Icons/icon_menu.png
     Images/hero_illustration.png
     Backgrounds/main_bg.png
   총 4개 에셋, 중복 다운로드 0건
   ...

   ### 임시 파일
   Assets/Screenshots/verify_*.png -- 삭제 가능
   Assets/Screenshots/resolution_*.png -- 삭제 가능
   ```

3. 추가 작업 안내:
   - 커스텀 폰트 적용 필요 시 안내
   - 스프라이트 교체 필요 시 안내
   - SafeArea 대응 필요 시 안내

## Error Handling

| 상황 | 대응 |
|------|------|
| Figma MCP 미연결 | MCP 서버 활성화 안내, 스크린샷 경로로 폴백 제안 |
| Figma URL 파싱 실패 | URL 형식 안내, 수동 fileKey/nodeId 입력 요청 |
| get_design_context 응답 과대 | get_metadata로 구조 파악 후 자식 노드별 개별 조회 |
| 에셋 다운로드 실패 | placeholder Image로 생성, 수동 교체 안내 |
| Bridge 연결 실패 | `unity-cli status`로 확인 안내 |
| TMP 리소스 미설치 | 자동 임포트 대기 (재시도) |
| GameObject 이름 중복 | 고유 이름 사용 (기능적 명칭) |
| 캡처 실패 (Edit Mode) | Play Mode 폴백 시도 |
| 해상도 변경 실패 | 수동 Game View 설정 안내 |
| 반복 횟수 초과 | 현재 상태 + 이슈 보고, 사용자 결정 |

## References

- `./references/new-commands.md` -- 추가 unity-cli 명령어 레퍼런스
- `./references/anchoring-strategy.md` -- 반응형 앵커링 패턴 가이드
- `./references/resolution-profiles.md` -- 다해상도 검증 프로파일
- `./references/figma-to-ugui-mapping.md` -- Figma 속성 → UGUI 매핑 상세
