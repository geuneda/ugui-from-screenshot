# UnityCliBridge.cs Changes

unity-cli 브릿지에 필요한 변경 사항 목록. 대상 파일: `unity-connector/Editor/UnityCliBridge.cs`

## Tool Registration

`ToolNames` 배열 (line 41-57)에 추가:

```csharp
"ui.panel.create",
"ui.layout.add",
"ui.recttransform.modify",
"ui.screenshot.capture",
"editor.gameview.resize"
```

## Method Modifications

### 1. EnsureCanvas (line 1794-1811)

**시그니처 변경**: `EnsureCanvas(string canvasName)` -> `EnsureCanvas(string canvasName, JObject arguments = null)`

**추가 파라미터**: referenceResolution, screenMatchMode, matchWidthOrHeight

**호출부 변경**: `ui.canvas.create` 핸들러에서 arguments 전달

### 2. ApplyRectTransform (line 2061-2068)

**추가 파라미터**: anchorMin, anchorMax, pivot (미지정 시 기존 (0.5,0.5) 유지)

### 3. 모든 UI Create 메서드

`CreateButton`, `CreateText`, `CreateImage`, `CreateToggle`, `CreateSlider`,
`CreateInputField`, `CreateScrollRect`

**변경**: `ResolveUiParent(arguments, canvas)` 호출 추가, parentName/parentId 지원

### 4. CreateText (line 1616-1626)

**추가 파라미터**: fontSize, fontStyle, alignment

### 5. CreateButton (line 1595-1614)

**추가 파라미터**: fontSize, fontStyle, textAlignment (라벨 텍스트)

### 6. CreateImage (line 1628-1639)

**추가 파라미터**: spritePath, imageType

## New Methods

### ResolveUiParent

```csharp
private static Transform ResolveUiParent(JObject arguments, GameObject canvasObject)
```

parentName/parentId로 부모 RectTransform 검색. 미지정 시 Canvas 반환.

### ApplyLayoutPadding (x2 overloads)

```csharp
private static void ApplyLayoutPadding(HorizontalOrVerticalLayoutGroup layout, JObject arguments)
private static void ApplyLayoutPadding(GridLayoutGroup layout, JObject arguments)
```

paddingLeft/Right/Top/Bottom 파라미터 적용.

### ParseTextAnchor, ParseFitMode, ParseFontStyle

Enum 파싱 헬퍼 메서드들.

## New Tools

### ui.panel.create

빈 RectTransform + 선택적 Image. 파라미터: name, canvasName, parentName, parentId,
anchorMin, anchorMax, pivot, anchoredPosition, size, color.

### ui.layout.add

Layout 컴포넌트 추가. 파라미터: name/id, layoutType(Horizontal/Vertical/Grid/ContentSizeFitter),
spacing, childAlignment, childForceExpand*, childControl*, padding*, cellSize, gridSpacing,
horizontalFit, verticalFit.

### ui.recttransform.modify

기존 RectTransform 수정. 파라미터: name/id, anchorMin, anchorMax, pivot,
anchoredPosition, size, offsetMin, offsetMax. 제공된 것만 수정.

### ui.screenshot.capture

Game View 캡처. 파라미터: width, height, outputPath.
EditorWindow 리플렉션 또는 Play Mode ScreenCapture 사용.

### editor.gameview.resize

Game View 해상도 변경. 파라미터: width, height.
GameViewSizes 리플렉션 API 사용.

## Backward Compatibility

모든 기존 파라미터의 기본값이 현재 하드코딩된 값과 동일하므로,
기존 명령어와 워크플로우는 변경 없이 동작함.
