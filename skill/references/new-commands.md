# New unity-cli Commands Reference

UGUI from Screenshot 스킬에 필요한 추가 unity-cli 명령어.

## UI Commands

### ui canvas.create (Enhanced)

기존 명령어에 CanvasScaler 파라미터 추가.

```bash
unity-cli ui canvas.create name=MyCanvas \
  referenceResolution=1440,3040 \
  screenMatchMode=Expand \
  matchWidthOrHeight=0.5
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | string | "Canvas" | Canvas 이름 |
| `referenceResolution` | Vector2 | 1920,1080 | CanvasScaler 기준 해상도 |
| `screenMatchMode` | string | "Expand" | Expand / Shrink / MatchWidthOrHeight |
| `matchWidthOrHeight` | float | 0.5 | Match 비율 (0=width, 1=height) |

---

### ui panel.create

빈 RectTransform 컨테이너. UI 계층의 기본 구성 단위.

```bash
unity-cli ui panel.create canvasName=UICanvas name=Header \
  anchorMin=0,1 anchorMax=1,1 pivot=0.5,1 \
  anchoredPosition=0,0 size=0,180 color=#1A1A2EFF
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | string | "Panel" | 패널 이름 |
| `canvasName` | string | "Canvas" | 부모 Canvas 이름 |
| `parentName` | string | null | 부모 RectTransform 이름 |
| `parentId` | int | null | 부모 InstanceID |
| `anchorMin` | Vector2 | 0.5,0.5 | 앵커 최소 |
| `anchorMax` | Vector2 | 0.5,0.5 | 앵커 최대 |
| `pivot` | Vector2 | 0.5,0.5 | 피봇 |
| `anchoredPosition` | Vector2 | 0,0 | 위치 |
| `size` | Vector2 | 200,200 | sizeDelta |
| `color` | string | null | 배경색 hex (지정 시 Image 추가) |

---

### ui text.create (Enhanced)

기존 명령어에 스타일링 파라미터 추가.

```bash
unity-cli ui text.create canvasName=UICanvas name=Title \
  parentName=Header text="Page Title" \
  fontSize=48 fontStyle=Bold alignment=MiddleCenter \
  color=#FFFFFFFF
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `fontSize` | float | 24 | 폰트 크기 |
| `fontStyle` | string | "Normal" | Normal/Bold/Italic/BoldAndItalic |
| `alignment` | string | "MiddleCenter" | TextAnchor enum 문자열 |
| `parentName` | string | null | 부모 RectTransform 이름 |
| `parentId` | int | null | 부모 InstanceID |
| `anchorMin` | Vector2 | 0.5,0.5 | 앵커 최소 |
| `anchorMax` | Vector2 | 0.5,0.5 | 앵커 최대 |
| `pivot` | Vector2 | 0.5,0.5 | 피봇 |

기존 파라미터(name, canvasName, text, color, anchoredPosition, size)는 그대로 유지.

---

### ui image.create (Enhanced)

기존 명령어에 스프라이트 파라미터 추가.

```bash
unity-cli ui image.create canvasName=UICanvas name=Icon \
  parentName=Header spritePath=Assets/Sprites/back.png \
  imageType=Simple size=64,64
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `spritePath` | string | null | 스프라이트 Asset 경로 |
| `imageType` | string | null | Simple/Sliced/Tiled/Filled |
| `parentName` | string | null | 부모 RectTransform 이름 |
| `parentId` | int | null | 부모 InstanceID |
| `anchorMin` | Vector2 | 0.5,0.5 | 앵커 최소 |
| `anchorMax` | Vector2 | 0.5,0.5 | 앵커 최대 |
| `pivot` | Vector2 | 0.5,0.5 | 피봇 |

기존 파라미터(name, canvasName, color, anchoredPosition, size)는 그대로 유지.

---

### ui button.create (Enhanced)

기존 명령어에 라벨 스타일링 + 부모 파라미터 추가.

```bash
unity-cli ui button.create canvasName=UICanvas name=SubmitBtn \
  parentName=Footer text=Submit \
  fontSize=32 fontStyle=Bold textAlignment=MiddleCenter \
  color=#2788FFFF textColor=#FFFFFFFF
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `fontSize` | float | 24 | 라벨 폰트 크기 |
| `fontStyle` | string | "Normal" | 라벨 폰트 스타일 |
| `textAlignment` | string | "MiddleCenter" | 라벨 정렬 |
| `parentName` | string | null | 부모 RectTransform 이름 |
| `parentId` | int | null | 부모 InstanceID |
| `anchorMin` | Vector2 | 0.5,0.5 | 앵커 최소 |
| `anchorMax` | Vector2 | 0.5,0.5 | 앵커 최대 |
| `pivot` | Vector2 | 0.5,0.5 | 피봇 |

기존 파라미터(name, canvasName, text, color, textColor, anchoredPosition, size)는 그대로 유지.

---

### ui layout.add

기존 GameObject에 Layout 컴포넌트 추가.

```bash
# Vertical Layout
unity-cli ui layout.add name=Content layoutType=Vertical \
  spacing=16 childAlignment=UpperCenter \
  childForceExpandWidth=true childForceExpandHeight=false \
  paddingLeft=24 paddingRight=24 paddingTop=16 paddingBottom=16

# Grid Layout
unity-cli ui layout.add name=Grid layoutType=Grid \
  cellSize=200,200 gridSpacing=16,16 childAlignment=UpperLeft

# Content Size Fitter
unity-cli ui layout.add name=Content layoutType=ContentSizeFitter \
  horizontalFit=Unconstrained verticalFit=PreferredSize
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name`/`id` | string/int | required | 대상 GameObject |
| `layoutType` | string | "Vertical" | Horizontal/Vertical/Grid/ContentSizeFitter |
| `spacing` | float | 0 | 자식 간격 (H/V) |
| `childAlignment` | string | "UpperLeft" | TextAnchor enum |
| `childForceExpandWidth` | bool | true | 자식 가로 확장 강제 |
| `childForceExpandHeight` | bool | true | 자식 세로 확장 강제 |
| `childControlWidth` | bool | true | 자식 가로 크기 제어 |
| `childControlHeight` | bool | true | 자식 세로 크기 제어 |
| `paddingLeft` | float | 0 | 왼쪽 패딩 |
| `paddingRight` | float | 0 | 오른쪽 패딩 |
| `paddingTop` | float | 0 | 위쪽 패딩 |
| `paddingBottom` | float | 0 | 아래쪽 패딩 |
| `cellSize` | Vector2 | 100,100 | Grid 셀 크기 |
| `gridSpacing` | Vector2 | 0,0 | Grid 간격 |
| `horizontalFit` | string | "Unconstrained" | CSF FitMode |
| `verticalFit` | string | "Unconstrained" | CSF FitMode |

---

### ui recttransform.modify

기존 RectTransform 속성 수정. 제공된 파라미터만 변경.

```bash
unity-cli ui recttransform.modify name=Header \
  anchorMin=0,0.9 anchorMax=1,1 \
  anchoredPosition=0,0 size=0,0 \
  offsetMin=0,0 offsetMax=0,0
```

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

---

## Editor Commands

### editor gameview.resize

Game View 해상도 변경.

```bash
unity-cli editor gameview.resize width=1440 height=3040
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `width` | int | 1920 | 가로 해상도 |
| `height` | int | 1080 | 세로 해상도 |

---

### ui screenshot.capture

Game View 캡처 후 PNG 파일 저장.

```bash
unity-cli ui screenshot.capture width=1440 height=3040 \
  outputPath=Assets/Screenshots/capture.png
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `width` | int | 1920 | 캡처 너비 |
| `height` | int | 1080 | 캡처 높이 |
| `outputPath` | string | required | 저장 경로 |

응답:
```json
{
  "width": 1440,
  "height": 3040,
  "format": "png",
  "outputPath": "Assets/Screenshots/capture.png"
}
```
