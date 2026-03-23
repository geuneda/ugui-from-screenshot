# Anchoring Strategy Reference

UGUI 반응형 레이아웃을 위한 앵커링 패턴 가이드.

## Core Concepts

### Anchor 좌표계
- `(0,0)` = 좌하단, `(1,1)` = 우상단
- `anchorMin == anchorMax` -> 고정 크기 (sizeDelta = 절대 크기)
- `anchorMin != anchorMax` -> 스트레치 (sizeDelta = 부모 대비 축소량)

### Pivot
- 회전/스케일/위치 기준점
- `(0.5, 0.5)` = 중앙, `(0, 1)` = 좌상단, `(1, 0)` = 우하단

---

## Common Patterns

### 1. Top Bar (Header)

전체 너비, 고정 높이, 상단 고정.

```
anchorMin = 0, 1
anchorMax = 1, 1
pivot     = 0.5, 1
anchoredPosition = 0, 0
size      = 0, {height}
```

CLI:
```bash
unity-cli ui panel.create name=Header \
  anchorMin=0,1 anchorMax=1,1 pivot=0.5,1 \
  anchoredPosition=0,0 size=0,180
```

### 2. Bottom Bar (Footer/TabBar)

전체 너비, 고정 높이, 하단 고정.

```
anchorMin = 0, 0
anchorMax = 1, 0
pivot     = 0.5, 0
anchoredPosition = 0, 0
size      = 0, {height}
```

CLI:
```bash
unity-cli ui panel.create name=BottomBar \
  anchorMin=0,0 anchorMax=1,0 pivot=0.5,0 \
  anchoredPosition=0,0 size=0,120
```

### 3. Content Area (Header/Footer 사이)

상하 바 사이 스트레치.

```
anchorMin = 0, 0
anchorMax = 1, 1
pivot     = 0.5, 0.5
offsetMin = 0, {bottomBarHeight}
offsetMax = 0, -{headerHeight}
```

CLI (생성 후 수정):
```bash
unity-cli ui panel.create name=Content \
  anchorMin=0,0 anchorMax=1,1 pivot=0.5,0.5

unity-cli ui recttransform.modify name=Content \
  offsetMin=0,120 offsetMax=0,-180
```

### 4. Center Fixed

화면 중앙, 고정 크기.

```
anchorMin = 0.5, 0.5
anchorMax = 0.5, 0.5
pivot     = 0.5, 0.5
anchoredPosition = 0, 0
size      = {width}, {height}
```

CLI:
```bash
unity-cli ui panel.create name=Dialog \
  anchorMin=0.5,0.5 anchorMax=0.5,0.5 pivot=0.5,0.5 \
  anchoredPosition=0,0 size=600,400
```

### 5. Full Screen Overlay

화면 전체 스트레치.

```
anchorMin = 0, 0
anchorMax = 1, 1
pivot     = 0.5, 0.5
offsetMin = 0, 0
offsetMax = 0, 0
```

CLI:
```bash
unity-cli ui panel.create name=Overlay \
  anchorMin=0,0 anchorMax=1,1 pivot=0.5,0.5

unity-cli ui recttransform.modify name=Overlay \
  offsetMin=0,0 offsetMax=0,0
```

### 6. Left-Anchored Element

좌측 고정, 세로 스트레치.

```
anchorMin = 0, 0
anchorMax = 0, 1
pivot     = 0, 0.5
anchoredPosition = 0, 0
size      = {width}, 0
```

### 7. Right-Anchored Element

우측 고정, 세로 스트레치.

```
anchorMin = 1, 0
anchorMax = 1, 1
pivot     = 1, 0.5
anchoredPosition = 0, 0
size      = {width}, 0
```

### 8. Proportional Columns (2등분)

```
Column1: anchorMin=0,0  anchorMax=0.5,1
Column2: anchorMin=0.5,0  anchorMax=1,1
```

### 9. Proportional Columns (3등분)

```
Column1: anchorMin=0,0      anchorMax=0.333,1
Column2: anchorMin=0.333,0  anchorMax=0.666,1
Column3: anchorMin=0.666,0  anchorMax=1,1
```

---

## Layout Group Combinations

### Horizontal Equal Tabs (Bottom Bar)

```bash
# 부모: 하단 고정
unity-cli ui panel.create name=TabBar \
  anchorMin=0,0 anchorMax=1,0 pivot=0.5,0 size=0,120

# Layout 추가
unity-cli ui layout.add name=TabBar layoutType=Horizontal \
  spacing=0 childAlignment=MiddleCenter \
  childForceExpandWidth=true childForceExpandHeight=true

# 자식: 각 탭 (크기는 Layout이 자동 결정)
unity-cli ui button.create name=Tab1 parentName=TabBar text=Home
unity-cli ui button.create name=Tab2 parentName=TabBar text=Search
unity-cli ui button.create name=Tab3 parentName=TabBar text=Profile
```

### Vertical Scroll List

```bash
# ScrollRect
unity-cli ui scrollrect.create canvasName=UICanvas name=ScrollList \
  anchorMin=0,0 anchorMax=1,1 pivot=0.5,0.5

# Content에 Vertical Layout 추가
unity-cli ui layout.add name=Content layoutType=Vertical \
  spacing=8 childAlignment=UpperCenter \
  childForceExpandWidth=true childForceExpandHeight=false

# Content Size Fitter
unity-cli ui layout.add name=Content layoutType=ContentSizeFitter \
  horizontalFit=Unconstrained verticalFit=PreferredSize
```

### Card Grid

```bash
unity-cli ui panel.create name=CardGrid \
  anchorMin=0,0 anchorMax=1,1 pivot=0.5,0.5

unity-cli ui layout.add name=CardGrid layoutType=Grid \
  cellSize=300,400 gridSpacing=16,16 childAlignment=UpperCenter \
  paddingLeft=16 paddingRight=16 paddingTop=16 paddingBottom=16
```

---

## Decision Guide

스크린샷 분석 시 앵커링 전략 결정 흐름:

```
요소가 화면 가장자리에 붙어있는가?
  YES -> 해당 가장자리 앵커 (top/bottom/left/right)
    가로 전체?
      YES -> anchorMin.x=0, anchorMax.x=1 (가로 스트레치)
      NO  -> 고정 너비
  NO  -> 부모 기준 상대 위치
    중앙 정렬?
      YES -> center anchor (0.5, 0.5)
      NO  -> 가장 가까운 가장자리 앵커

요소들이 균등 배치되어 있는가?
  YES -> Layout Group 사용
    가로 배열? -> HorizontalLayoutGroup
    세로 배열? -> VerticalLayoutGroup
    격자 배열? -> GridLayoutGroup

요소가 스크롤 가능한가?
  YES -> ScrollRect + VerticalLayoutGroup + ContentSizeFitter
```
