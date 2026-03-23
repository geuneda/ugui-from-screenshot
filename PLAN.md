# UGUI from Design - Implementation Plan

## 1. Overview

사용자가 **Figma URL** 또는 **UI 디자인 스크린샷**을 제공하면, Claude Code가 unity-cli를 통해 UGUI를 자동 구성하고,
반복적인 검증-수정 루프를 거쳐 90%+ 일치를 달성하며, 다양한 해상도에서 대응을 점검하는 시스템.

### 핵심 기술 스택
- **Figma MCP**: 디자인 메타데이터/스크린샷/에셋을 직접 조회 (권장 입력)
- **Claude Code**: 멀티모달 비전으로 스크린샷 분석 + CLI 명령 실행 (폴백 입력)
- **unity-cli**: Unity Editor HTTP 브릿지를 통한 UGUI 조작
- **Unity UGUI**: Canvas, RectTransform, Layout 기반 UI 시스템

### 입력 소스
| 입력 | 경로 | 장점 |
|------|------|------|
| Figma URL | Figma MCP → 정밀 메타데이터 (위치/크기/색상/에셋 정확값) | 높은 정확도, 에셋 자동 다운로드 |
| 스크린샷 파일 | Claude 비전 → 추정값 | MCP 불필요, 모든 디자인 도구 지원 |

---

## 2. unity-cli Bridge 개선 사양

### 2.1 EnsureCanvas - CanvasScaler 파라미터 지원

**현재 상태**: `referenceResolution=1920x1080`, `ScaleWithScreenSize` 하드코딩

**변경 사항**: `EnsureCanvas(string canvasName, JObject arguments = null)` 시그니처 변경

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `referenceResolution` | Vector2 | 1920,1080 | CanvasScaler 기준 해상도 |
| `screenMatchMode` | string | "Expand" | "Expand", "Shrink", "MatchWidthOrHeight" |
| `matchWidthOrHeight` | float | 0.5 | MatchWidthOrHeight 모드 시 비율 (0=width, 1=height) |

**구현 핵심**:
```csharp
scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
scaler.referenceResolution = ParseVector2(arguments?["referenceResolution"] as JArray, new Vector2(1920f, 1080f));
scaler.screenMatchMode = (arguments?.Value<string>("screenMatchMode")) switch
{
    "Shrink" => CanvasScaler.ScreenMatchMode.Shrink,
    "MatchWidthOrHeight" => CanvasScaler.ScreenMatchMode.MatchWidthOrHeight,
    _ => CanvasScaler.ScreenMatchMode.Expand,
};
scaler.matchWidthOrHeight = arguments?["matchWidthOrHeight"]?.Value<float>() ?? 0.5f;
```

**하위 호환**: 기존 호출부(`CreateButton`, `CreateText` 등)는 인자 없이 호출하므로 기본값 적용.

---

### 2.2 ApplyRectTransform - 앵커/피봇 파라미터

**현재 상태**: `anchorMin = anchorMax = pivot = (0.5, 0.5)` 하드코딩

**변경 사항**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `anchorMin` | Vector2 | 0.5,0.5 | RectTransform anchorMin |
| `anchorMax` | Vector2 | 0.5,0.5 | RectTransform anchorMax |
| `pivot` | Vector2 | 0.5,0.5 | RectTransform pivot |

**구현**:
```csharp
private static void ApplyRectTransform(RectTransform rt, JObject args, Vector2 defaultSize)
{
    rt.anchorMin = ParseVector2(args["anchorMin"] as JArray, new Vector2(0.5f, 0.5f));
    rt.anchorMax = ParseVector2(args["anchorMax"] as JArray, new Vector2(0.5f, 0.5f));
    rt.pivot = ParseVector2(args["pivot"] as JArray, new Vector2(0.5f, 0.5f));
    rt.anchoredPosition = ParseVector2(args["anchoredPosition"] as JArray, Vector2.zero);
    rt.sizeDelta = ParseVector2(args["size"] as JArray, defaultSize);
}
```

**하위 호환**: 미지정 시 기존 동작 (center anchor) 유지.

---

### 2.3 Parent 파라미터

**현재 상태**: 모든 UI 요소가 Canvas 직속 자식으로 생성

**변경 사항**: 모든 UI 생성 메서드에 `parentName`/`parentId` 지원

```csharp
private static Transform ResolveUiParent(JObject arguments, GameObject canvasObject)
{
    var parentName = arguments.Value<string>("parentName");
    var parentId = arguments["parentId"]?.Value<int?>();

    if (parentId.HasValue)
    {
        var parent = EditorUtility.InstanceIDToObject(parentId.Value) as GameObject;
        if (parent != null && parent.GetComponent<RectTransform>() != null)
            return parent.transform;
    }

    if (!string.IsNullOrEmpty(parentName))
    {
        var parent = GameObject.Find(parentName);
        if (parent != null && parent.GetComponent<RectTransform>() != null)
            return parent.transform;
    }

    return canvasObject.transform;
}
```

적용 위치: `CreateButton`, `CreateText`, `CreateImage`, `CreateToggle`, `CreateSlider`,
`CreateInputField`, `CreateScrollRect` 및 새로 추가되는 `ui.panel.create`.

---

### 2.4 Text 스타일링 파라미터

**현재 상태**: `fontSize=24`, `FontStyle.Normal`, `TextAnchor.MiddleCenter` 하드코딩

**추가 파라미터**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `fontSize` | float | 24 | 폰트 크기 |
| `fontStyle` | string | "Normal" | "Normal", "Bold", "Italic", "BoldAndItalic" |
| `alignment` | string | "MiddleCenter" | TextAnchor enum 문자열 |

적용 대상: `CreateText`, `CreateButton` (라벨), `CreateUiTextChild` 호출부.

---

### 2.5 Image 스프라이트 할당

**현재 상태**: 항상 `UISprite.psd` 사용

**추가 파라미터**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `spritePath` | string | null | Asset 경로 (예: "Assets/Sprites/icon.png") |
| `imageType` | string | null | "Simple", "Sliced", "Tiled", "Filled" |

```csharp
var spritePath = arguments.Value<string>("spritePath");
if (!string.IsNullOrEmpty(spritePath))
{
    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
    if (sprite != null) image.sprite = sprite;
}
```

---

### 2.6 새 도구: `ui.panel.create`

빈 RectTransform 컨테이너. UI 계층 구성의 기본 단위.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | string | "Panel" | GameObject 이름 |
| `canvasName` | string | "Canvas" | 부모 Canvas 이름 |
| `parentName` | string | null | 부모 RectTransform 이름 |
| `parentId` | int | null | 부모 InstanceID |
| `anchorMin` | Vector2 | 0.5,0.5 | 앵커 최소 |
| `anchorMax` | Vector2 | 0.5,0.5 | 앵커 최대 |
| `pivot` | Vector2 | 0.5,0.5 | 피봇 |
| `anchoredPosition` | Vector2 | 0,0 | 앵커 기준 위치 |
| `size` | Vector2 | 200,200 | sizeDelta |
| `color` | string | null | 배경색 (지정 시 Image 추가) |

---

### 2.7 새 도구: `ui.layout.add`

기존 GameObject에 Layout 컴포넌트 추가.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name`/`id` | string/int | 대상 GameObject |
| `layoutType` | string | "Horizontal", "Vertical", "Grid", "ContentSizeFitter" |
| `spacing` | float | 자식 간 간격 |
| `childAlignment` | string | TextAnchor enum 문자열 |
| `childForceExpandWidth` | bool | 자식 가로 확장 강제 |
| `childForceExpandHeight` | bool | 자식 세로 확장 강제 |
| `childControlWidth` | bool | 자식 가로 크기 제어 |
| `childControlHeight` | bool | 자식 세로 크기 제어 |
| `paddingLeft/Right/Top/Bottom` | float | 레이아웃 패딩 |
| `cellSize` | Vector2 | Grid 셀 크기 |
| `gridSpacing` | Vector2 | Grid 간격 |
| `horizontalFit` | string | CSF FitMode |
| `verticalFit` | string | CSF FitMode |

---

### 2.8 새 도구: `ui.recttransform.modify`

기존 RectTransform 속성 수정. 생성 후 위치/크기/앵커 조정에 사용.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name`/`id` | string/int | 대상 GameObject |
| `anchorMin` | Vector2 | (선택) anchorMin |
| `anchorMax` | Vector2 | (선택) anchorMax |
| `pivot` | Vector2 | (선택) pivot |
| `anchoredPosition` | Vector2 | (선택) 위치 |
| `size` | Vector2 | (선택) sizeDelta |
| `offsetMin` | Vector2 | (선택) offsetMin |
| `offsetMax` | Vector2 | (선택) offsetMax |

제공된 파라미터만 수정하고, 나머지는 기존 값 유지.

---

### 2.9 새 도구: `ui.screenshot.capture`

Game View 캡처 후 파일 저장.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `width` | int | 1920 | 캡처 너비 |
| `height` | int | 1080 | 캡처 높이 |
| `outputPath` | string | required | 저장 경로 (Assets/ 하위 권장) |

**구현 전략**:
1. 우선: `EditorWindow` Game View 리플렉션으로 RenderTexture 접근
2. 폴백: Play Mode 진입 -> `ScreenCapture.CaptureScreenshotAsTexture()` -> Play Mode 종료
3. 결과: 파일 경로 반환 (base64 인라인은 크기 문제로 지양)

**ScreenSpaceOverlay 캡처 이슈**:
Overlay Canvas는 Camera.Render()로 캡처 불가. 해결 방안:
- 임시로 ScreenSpaceCamera로 전환 -> 캡처 -> Overlay 복원
- 또는 Play Mode에서 ScreenCapture API 사용

---

### 2.10 새 도구: `editor.gameview.resize`

Game View 해상도 변경. 다해상도 검증에 필수.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `width` | int | 1920 | 가로 해상도 |
| `height` | int | 1080 | 세로 해상도 |

**구현**: `UnityEditor.GameViewSizes` 리플렉션 API로 커스텀 해상도 추가/선택.

```csharp
var gameViewType = Type.GetType("UnityEditor.GameView, UnityEditor");
var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
// GameViewSizes -> GetGroup -> AddCustomSize -> selectedSizeIndex 설정
```

---

## 3. Skill 워크플로우 상세

### Phase 1: Setup & Design Acquisition

```
입력 판별:
  - Figma URL → Path A (Figma MCP)
  - 스크린샷 파일 → Path B (비전 분석)

Path A: Figma MCP
  1. URL 파싱: figma.com/design/:fileKey/:fileName?node-id=:nodeId
  2. get_metadata(fileKey, nodeId) → 페이지 노드 트리 (타입/이름/위치/크기)
  3. get_design_context(fileKey, nodeId) → 상세 디자인 데이터 + 스크린샷 + 에셋 URL
  4. get_screenshot(fileKey, nodeId) → 검증용 레퍼런스 스크린샷
  5. 에셋 다운로드: curl → Assets/FigmaAssets/ → editor refresh
  6. 기준 해상도 = 최상위 Frame 크기
  7. ScreenMatchMode 확인 (기본: Expand)
  8. Canvas 생성

Path B: 스크린샷 (기존)
  1. 사용자로부터 레퍼런스 스크린샷 수신 (파일 경로)
  2. Read 도구로 스크린샷 분석 (Claude 비전)
  3. 기준 해상도 확인 (사용자 제공 또는 질문)
  4. ScreenMatchMode 확인 (기본: Expand)
  5. Canvas 생성

공통:
  unity-cli ui canvas.create name=UICanvas \
    referenceResolution=1440,3040 screenMatchMode=Expand
```

### Phase 2: Analysis & Build

```
1. UI 요소 식별:
   Figma: get_design_context 응답에서 정확한 값 추출
   스크린샷: Claude 비전으로 추정
   - 타입 (Button, Text, Image, Panel, ScrollView 등)
   - 위치 (기준 해상도 내 픽셀 좌표)
   - 크기 (픽셀 단위)
   - 색상 (hex 값)
   - 텍스트 내용 및 스타일
   - 계층 관계

2. Figma 속성 → UGUI 매핑 (Figma 경로):
   - Auto Layout → Layout Group (Horizontal/Vertical)
   - Constraints (LEFT+RIGHT) → 스트레치 앵커
   - Constraints (TOP) → anchorMin=_,1 anchorMax=_,1 pivot=_,1
   - Fill color → color hex
   - Text properties → fontSize, fontStyle, alignment
   - 에셋 URL → spritePath (다운로드 후)

2b. 앵커링 전략 결정 (스크린샷 경로, references/anchoring-strategy.md 참조):
   - 상단 바: anchorMin=(0,1) anchorMax=(1,1) pivot=(0.5,1)
   - 하단 바: anchorMin=(0,0) anchorMax=(1,0) pivot=(0.5,0)
   - 콘텐츠: stretch anchor + offset
   - 고정 요소: center anchor + fixed size

3. 빌드 순서 (부모 -> 자식):
   a. 최상위 Panel 컨테이너들
   b. 하위 Panel/Layout 구조
   c. 리프 요소 (Text, Button, Image)
   d. Layout 컴포넌트 추가

4. CLI 명령 실행:
   unity-cli ui panel.create canvasName=UICanvas name=Header \
     anchorMin=0,1 anchorMax=1,1 pivot=0.5,1 size=0,180 color=#1A1A2EFF
   unity-cli ui text.create canvasName=UICanvas name=Title \
     parentName=Header fontSize=48 fontStyle=Bold alignment=MiddleCenter ...
```

### Phase 3: Verification Loop

```
반복 (최대 5회):
  1. 해상도 설정:
     unity-cli editor gameview.resize width=1440 height=3040
  2. 스크린샷 캡처:
     unity-cli ui screenshot.capture width=1440 height=3040 \
       outputPath=Assets/Screenshots/verify_{iteration}.png
  3. Claude 비전으로 비교:
     - STRUCTURE (30%): 모든 요소 존재 여부 및 계층
     - POSITION (25%): 위치 정확도 (5% 이내)
     - SIZE (20%): 크기 정확도 (10% 이내)
     - COLOR (15%): 색상 일치 (hex 비교)
     - TEXT (10%): 텍스트 내용 정확도
  4. 가중 평균 산출
  5. 90% 이상이면 PASS -> Phase 4로
  6. 미달 시 차이점 목록화 -> 수정 명령 실행 -> 반복

수정에 사용하는 주요 명령:
  - ui recttransform.modify name=Header anchoredPosition=0,-10 size=0,200
  - component update name=Title type=TMPro.TextMeshProUGUI values={"fontSize":52}
  - ui layout.add name=Content layoutType=Vertical spacing=16
```

### Phase 4: Multi-Resolution Validation

```
해상도 목록:
  - 2160x1620 (iPad, 가로 우세)
  - 2532x1170 (iPhone, 세로 우세)
  - 2340x1080 (Galaxy S21)
  - 2280x1080 (중급 Android)
  - 3040x1440 (Galaxy S10+, 기준 해상도)

각 해상도에서 (최대 3회 반복):
  1. editor gameview.resize로 해상도 변경
  2. screenshot.capture로 캡처
  3. 검증 기준:
     - 요소 클리핑 없음
     - 요소 오버랩 없음
     - 텍스트 가독성 유지
     - 비율 15% 이내 유지
  4. 문제 발견 시:
     - 앵커 조정 (고정 -> 스트레치 등)
     - Layout 컴포넌트 추가/수정
     - ContentSizeFitter 적용
  5. 수정 후 모든 해상도 재검증

해상도 간 공통 문제 패턴:
  - 가로/세로 비율 차이 큰 경우 -> Layout + 앵커 스트레치 조합
  - 작은 해상도에서 텍스트 잘림 -> ContentSizeFitter + overflow 설정
  - 큰 해상도에서 여백 과다 -> maxWidth 제한 또는 비율 앵커
```

### Phase 5: Complete

```
1. Game View를 기준 해상도로 복원
2. 결과 보고:
   - 최종 일치율 (Phase 3 결과)
   - 해상도별 검증 결과 테이블
   - 수정된 사항 목록
   - 생성된 UI 계층 구조
3. 임시 스크린샷 파일 목록 제시 (삭제 여부 사용자 선택)
4. 추가 수정 필요 시 사용자에게 안내
```

---

## 4. Error Handling

| 상황 | 대응 |
|------|------|
| Figma MCP 미연결 | MCP 서버 활성화 안내, 스크린샷 경로로 폴백 제안 |
| Figma URL 파싱 실패 | URL 형식 안내, 수동 fileKey/nodeId 입력 요청 |
| get_design_context 응답 과대 | get_metadata로 구조 파악 후 자식 노드별 개별 조회 |
| 에셋 다운로드 실패 | placeholder Image로 생성, 수동 교체 안내 |
| TMP 리소스 미설치 | 자동 임포트 대기 (기존 로직) |
| Canvas 이름 충돌 | EnsureCanvas가 기존 Canvas 재사용 |
| GameObject 이름 중복 | 고유 접두사 사용 (UGUI_ prefix) |
| 스크린샷 캡처 실패 (Edit Mode) | Play Mode 폴백 시도 |
| 해상도 변경 실패 | 사용자에게 수동 Game View 설정 안내 |
| 5회 반복 후 90% 미달 | 현재 상태 보고 + 사용자 판단 요청 |
| 3회 반복 후 해상도 미통과 | 해당 해상도 이슈 보고 + 사용자 판단 요청 |

---

## 5. Implementation Sequence

### Phase A: Bridge 개선 (unity-cli repo) -- DONE
1. [x] `ApplyRectTransform` 앵커/피봇 파라미터 추가
2. [x] `ResolveUiParent` 헬퍼 + 전체 Create 메서드 통합
3. [x] `EnsureCanvas` CanvasScaler 파라미터 추가
4. [x] Text 스타일링 파라미터 추가
5. [x] Image spritePath 파라미터 추가
6. [x] `ui.panel.create` 도구 추가
7. [x] `ui.layout.add` 도구 추가
8. [x] `ui.recttransform.modify` 도구 추가
9. [x] `ui.screenshot.capture` 도구 추가
10. [x] `editor.gameview.resize` 도구 추가
11. [x] `ToolNames` 배열 업데이트
12. [x] `commands.md` 문서 업데이트

### Phase B: Skill 생성 -- DONE
13. [x] `SKILL.md` 작성 (Figma MCP + 스크린샷 이중 입력 지원)
14. [x] `references/` 작성 (new-commands, anchoring-strategy, resolution-profiles, figma-to-ugui-mapping)
15. [x] 로컬 스킬 디렉토리에 설치

### Phase C: 검증
16. [ ] 기존 기능 회귀 테스트 (`verify-editor.sh`)
17. [ ] 새 명령어 수동 테스트
18. [ ] Figma URL로 E2E 테스트
19. [ ] 스크린샷 파일로 E2E 테스트

---

## 6. Known Limitations

- **폰트**: TMP 기본 폰트만 사용 가능. 커스텀 폰트 에셋은 수동 설정 필요
- **그라디언트/그림자**: UI 효과(Shadow, Outline, Gradient)는 자동화 범위 밖
- **애니메이션**: 전환 애니메이션은 포함하지 않음
- **Safe Area**: 노치/홀 대응은 별도 SafeArea 스크립트 필요
- **스크린샷 정확도**: Claude 비전 기반(스크린샷 경로)이므로 픽셀 단위 정밀도는 아님
- **Figma 정확도**: Figma MCP 경로는 정밀하지만, Auto Layout 중첩이 복잡한 경우 좌표 변환 오차 가능
- **Figma MCP 의존**: Figma MCP 서버가 비활성이면 스크린샷 경로로만 작동
- **에셋 형식**: Figma에서 SVG로 내보내진 에셋은 Unity에서 직접 사용 불가 (PNG 변환 필요)
- **복잡한 UI**: 중첩 ScrollView, 커스텀 셰이더 UI 등은 수동 보완 필요
