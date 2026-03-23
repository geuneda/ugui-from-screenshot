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

### Figma 디자인 컨텍스트에서 앵커 결정

`get_design_context`가 반환하는 Tailwind CSS 클래스에서 레이아웃 의도를 분석:

```
Tailwind 클래스 분석 순서:

1) 최상위 컨테이너의 position/size 확인
   - `size-full` 또는 `w-full h-full` → 풀 스트레치
   - `absolute` + 위치값 → 해당 가장자리 앵커
   - 고정 w/h (px 값) → 고정 크기

2) Flex/Grid 레이아웃 확인
   - `flex flex-col` → Vertical Layout Group
   - `flex` (기본) → Horizontal Layout Group
   - `grid grid-cols-[...]` → 비율 앵커 분할 또는 Grid Layout
   - `gap-{n}` → Layout spacing

3) 자식 요소의 크기 결정 확인
   - `flex-[1_0_0]` → 부모 내 비율 채우기 (스트레치)
   - `shrink-0` + 고정 크기 → 고정 크기 요소
   - `justify-self-stretch` → 가로 스트레치
   - `self-stretch` → 세로 스트레치

4) 정렬 확인
   - `items-center` → 교차축 중앙
   - `justify-between` → 양쪽 정렬
   - `justify-center` → 주축 중앙
```

### 통합 결정 플로우차트

```
[요소 분석 시작]
  │
  ├─ 화면 가장자리에 붙어있는가?
  │   ├─ 상단 + 가로 전체 → Top Stretch (anchorMin=0,1 anchorMax=1,1)
  │   ├─ 하단 + 가로 전체 → Bottom Stretch (anchorMin=0,0 anchorMax=1,0)
  │   ├─ 좌측 + 세로 전체 → Left Stretch (anchorMin=0,0 anchorMax=0,1)
  │   └─ 우측 + 세로 전체 → Right Stretch (anchorMin=1,0 anchorMax=1,1)
  │
  ├─ 부모 전체를 채우는가?
  │   └─ YES → Full Stretch (anchorMin=0,0 anchorMax=1,1) + offsetMin/offsetMax
  │
  ├─ 형제 요소와 나란히 배치되어 있는가?
  │   ├─ 가로 나란히 → 방법 선택:
  │   │   ├─ 비율 고정 → 비율 앵커 (anchorMin.x=ratio1, anchorMax.x=ratio2)
  │   │   └─ 동적 분배 → Horizontal Layout Group
  │   └─ 세로 나란히 → Vertical Layout Group
  │
  ├─ 중앙에 독립적으로 위치하는가?
  │   └─ YES → Center Fixed (anchorMin=0.5,0.5 anchorMax=0.5,0.5)
  │
  └─ 그 외 → 부모 기준 가장 가까운 가장자리 앵커 + 고정 크기
```

### Figma Auto Layout → UGUI 변환 패턴

| Figma Auto Layout 설정 | UGUI 구현 |
|------------------------|-----------|
| direction=VERTICAL, 자식 FILL 가로 | VLG + childForceExpandWidth=true |
| direction=HORIZONTAL, 자식 FILL 세로 | HLG + childForceExpandHeight=true |
| direction=HORIZONTAL, 자식 HUG | HLG + 자식에 ContentSizeFitter |
| spacing=auto (space-between) | HLG/VLG의 childForceExpand로 근사 |
| padding (asymmetric) | paddingLeft/Right/Top/Bottom 개별 설정 |

### 다해상도 안전 체크리스트

빌드 완료 후 확인:

```
□ 가로 전체 너비 요소에 anchorMin.x=0, anchorMax.x=1 사용했는가?
□ 상단 고정 요소에 anchorMin.y=1, anchorMax.y=1, pivot.y=1 사용했는가?
□ 하단 고정 요소에 anchorMin.y=0, anchorMax.y=0, pivot.y=0 사용했는가?
□ 고정 크기 요소에만 sizeDelta로 크기를 지정했는가?
□ 스트레치 요소에 offsetMin/offsetMax를 사용했는가?
□ 2-column 레이아웃에 비율 앵커 또는 Layout Group을 사용했는가?
□ 모든 요소의 앵커가 center(0.5,0.5)로 되어있지 않은가? (경고!)
```
