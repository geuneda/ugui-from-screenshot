# Figma → UGUI 매핑 레퍼런스

## Figma MCP 도구 사용 순서

1. **URL 파싱**: `figma.com/design/:fileKey/:fileName?node-id=:nodeId` → fileKey, nodeId 추출
2. **`get_metadata`**: 페이지 전체 노드 트리 (ID, 타입, 이름, 위치, 크기)
3. **`get_design_context`**: 노드별 상세 디자인 데이터 (코드, 색상, 타이포그래피, 레이아웃, 에셋 URL, 스크린샷)
4. **`get_screenshot`**: 비교 검증용 레퍼런스 스크린샷

## 노드 타입 → UGUI 요소

| Figma 노드 타입 | UGUI 명령 | 비고 |
|-----------------|-----------|------|
| Frame (컨테이너) | `ui panel.create` | Auto Layout이면 layout.add 추가 |
| Rectangle | `ui image.create` | fill color 적용 |
| Text | `ui text.create` | fontSize, fontStyle, alignment 매핑 |
| Instance (Button) | `ui button.create` | 내부 Text 노드에서 label 추출 |
| Instance (Toggle) | `ui toggle.create` | |
| Instance (Slider) | `ui slider.create` | |
| Instance (InputField) | `ui inputfield.create` | placeholder 텍스트 추출 |
| Image/Vector | `ui image.create` + `spritePath` | 에셋 다운로드 후 할당 |
| Group | `ui panel.create` | 논리적 그룹핑 |
| Component | 구성 요소에 따라 분기 | metadata로 내부 구조 확인 |

## 레이아웃 매핑

### Auto Layout → Layout Group

| Figma Auto Layout | UGUI |
|-------------------|------|
| direction: HORIZONTAL | `ui layout.add layoutType=Horizontal` |
| direction: VERTICAL | `ui layout.add layoutType=Vertical` |
| itemSpacing | `spacing={value}` |
| paddingLeft/Right/Top/Bottom | `paddingLeft={v} paddingRight={v} ...` |
| primaryAxisAlignItems: CENTER | `childAlignment=MiddleCenter` |
| primaryAxisAlignItems: MIN | `childAlignment=UpperLeft` (V) / `childAlignment=MiddleLeft` (H) |
| primaryAxisAlignItems: MAX | `childAlignment=LowerRight` 등 |
| counterAxisAlignItems | 교차축 정렬 → childAlignment 조합 |

### Constraints → Anchor

| Figma Constraint | UGUI anchorMin/anchorMax |
|-----------------|------------------------|
| LEFT + RIGHT | `anchorMin=0,_ anchorMax=1,_` (가로 스트레치) |
| TOP + BOTTOM | `anchorMin=_,0 anchorMax=_,1` (세로 스트레치) |
| LEFT only | `anchorMin=0,_ anchorMax=0,_` (왼쪽 고정) |
| RIGHT only | `anchorMin=1,_ anchorMax=1,_` (오른쪽 고정) |
| TOP only | `anchorMin=_,1 anchorMax=_,1 pivot=_,1` (상단 고정) |
| BOTTOM only | `anchorMin=_,0 anchorMax=_,0 pivot=_,0` (하단 고정) |
| CENTER (horizontal) | `anchorMin=0.5,_ anchorMax=0.5,_` |
| CENTER (vertical) | `anchorMin=_,0.5 anchorMax=_,0.5` |
| SCALE | `anchorMin=0,0 anchorMax=1,1` + offset 계산 |

### Sizing Mode

| Figma Sizing | UGUI |
|-------------|------|
| FIXED | `size={width},{height}` |
| FILL | 부모 기준 스트레치 앵커 + `offsetMin/offsetMax` |
| HUG | `ContentSizeFitter` (horizontalFit/verticalFit=PreferredSize) |

## 색상 매핑

Figma → UGUI 색상 변환:

```
Figma RGBA (0~1): { r: 0.1, g: 0.1, b: 0.18, a: 1.0 }
→ Hex: #1A1A2EFF
→ unity-cli color=#1A1A2EFF
```

변환 공식: `hex = #RRGGBBAA` where `RR = round(r * 255).toString(16)`

## 타이포그래피 매핑

| Figma | UGUI |
|-------|------|
| fontSize | `fontSize={value}` |
| fontWeight 400 (Regular) | `fontStyle=Normal` |
| fontWeight 700 (Bold) | `fontStyle=Bold` |
| fontWeight 400 + italic | `fontStyle=Italic` |
| fontWeight 700 + italic | `fontStyle=BoldAndItalic` |
| textAlignHorizontal: LEFT | `alignment=MiddleLeft` |
| textAlignHorizontal: CENTER | `alignment=MiddleCenter` |
| textAlignHorizontal: RIGHT | `alignment=MiddleRight` |
| textAlignVertical: TOP | alignment prefix = Upper |
| textAlignVertical: CENTER | alignment prefix = Middle |
| textAlignVertical: BOTTOM | alignment prefix = Lower |

조합 예시: textAlignHorizontal=CENTER + textAlignVertical=TOP → `alignment=UpperCenter`

## 좌표 변환

Figma 좌표계 (좌상단 원점, Y축 아래) → UGUI 좌표계 (앵커 기준, Y축 위).

**핵심 원칙**: 앵커 타입에 따라 좌표 계산 방식이 완전히 다르다. 모든 요소를 center anchor로 배치하면 기준 해상도에서만 동작하므로 반드시 요소 역할에 맞는 앵커를 선택한 뒤 해당 공식을 적용한다.

### 앵커 선택 기준

```
1) 화면 가장자리에 붙은 바/헤더 → 해당 가장자리 스트레치 앵커
2) 부모 전체를 채우는 배경  → 풀 스트레치 (0,0 ~ 1,1)
3) 화면 중앙의 카드/다이얼로그 → center anchor (0.5,0.5)
4) 나란한 column/row → 비율 앵커 또는 Layout Group
5) 부모 내부 자식 (고정 크기) → 부모 기준 상대 위치 앵커
```

### 1. Center Anchor (고정 크기, 중앙 배치)

`anchorMin=0.5,0.5 anchorMax=0.5,0.5 pivot=0.5,0.5`

```
anchoredPosition.x = figma.x + figma.w/2 - parentW/2
anchoredPosition.y = -(figma.y + figma.h/2 - parentH/2)
sizeDelta = (figma.w, figma.h)
```

예시: parentW=984, parentH=666, 자식 x=33, y=136, w=521.5, h=386
```
x = 33 + 260.75 - 492 = -198.25
y = -(136 + 193 - 333) = 4
→ anchoredPosition=-198.25,4 size=521.5,386
```

### 2. Top Stretch (상단 고정, 가로 스트레치)

`anchorMin=0,1 anchorMax=1,1 pivot=0.5,1`

```
anchoredPosition = (0, 0)        # 상단 기준이므로 y=0
sizeDelta = (0, figma.h)         # 가로는 스트레치(0), 세로는 고정
offsetMin.x = figma.marginLeft   # 좌측 여백 (0이면 전체 너비)
offsetMax.x = -figma.marginRight # 우측 여백 (0이면 전체 너비)
```

예시: 헤더 h=180, 좌우 여백 없음
```
→ anchorMin=0,1 anchorMax=1,1 pivot=0.5,1
  anchoredPosition=0,0 size=0,180
```

### 3. Bottom Stretch (하단 고정, 가로 스트레치)

`anchorMin=0,0 anchorMax=1,0 pivot=0.5,0`

```
anchoredPosition = (0, 0)
sizeDelta = (0, figma.h)
offsetMin.x = figma.marginLeft
offsetMax.x = -figma.marginRight
```

### 4. Full Stretch (상하좌우 스트레치)

`anchorMin=0,0 anchorMax=1,1 pivot=0.5,0.5`

```
offsetMin = (marginLeft, marginBottom)
offsetMax = (-marginRight, -marginTop)
sizeDelta = (0, 0)               # 앵커가 크기 결정
```

Figma 값에서 margin 계산:
```
marginLeft   = figma.x
marginRight  = parentW - figma.x - figma.w
marginTop    = figma.y
marginBottom = parentH - figma.y - figma.h
```

예시: 콘텐츠 영역 (header=180 아래, footer=120 위)
```
→ anchorMin=0,0 anchorMax=1,1
  offsetMin=0,120 offsetMax=0,-180
```

### 5. 비율 앵커 (Proportional Column)

수평으로 나란한 프레임을 비율로 분배:

```
ratioStart = figma.x / parentW
ratioEnd   = (figma.x + figma.w) / parentW
→ anchorMin=(ratioStart, 0) anchorMax=(ratioEnd, 1)
  offsetMin=(gapHalf, padding) offsetMax=(-gapHalf, -padding)
```

예시: parent=918, col1 x=0 w=521.5, gap=24, col2 x=545.5 w=372.5
```
Col1: anchorMin=0,0 anchorMax=0.568,1     # 521.5/918 ≈ 0.568
Col2: anchorMin=0.594,0 anchorMax=1,1     # 545.5/918 ≈ 0.594
각각 offsetMin/offsetMax로 내부 padding 설정
```

### 6. Left-Anchored (좌측 고정)

`anchorMin=0,0.5 anchorMax=0,0.5 pivot=0,0.5` (또는 세로 스트레치 시 y=0~1)

```
anchoredPosition.x = figma.x
anchoredPosition.y = -(figma.y + figma.h/2 - parentH/2)
sizeDelta = (figma.w, figma.h)
```

### 7. Right-Anchored (우측 고정)

`anchorMin=1,0.5 anchorMax=1,0.5 pivot=1,0.5`

```
anchoredPosition.x = -(parentW - figma.x - figma.w)
anchoredPosition.y = -(figma.y + figma.h/2 - parentH/2)
sizeDelta = (figma.w, figma.h)
```

### 변환 순서 요약

```
1. 요소 역할 판별 → 앵커 프리셋 선택
2. 해당 앵커 공식으로 좌표 계산
3. panel.create에 anchorMin, anchorMax, pivot 명시
4. 스트레치 앵커면 recttransform.modify로 offsetMin/offsetMax 설정
5. Layout Group 사용 시 자식 좌표는 Layout이 자동 결정 (생략 가능)
```

## 에셋 다운로드 및 Sprite 임포트 흐름

### 전체 파이프라인

```
에셋 URL 수집 → 분류 → 중복 확인 → curl 다운로드 → editor refresh → asset import-texture → ui image.create
```

### 폴더 구조

```
Assets/FigmaAssets/
  Icons/          -- 작은 아이콘 (≤64px, icon/ic/arrow/chevron/close/check 등)
  Images/         -- 컨텐츠 이미지, 일러스트, 사진, 원화
  Backgrounds/    -- 전체 화면/섹션 배경 (bg/background/wallpaper 등)
  Buttons/        -- 버튼 전용 이미지 (btn/button 등)
  Misc/           -- 위에 해당하지 않는 에셋
```

분류 판단 기준:
| 분류 | 조건 |
|------|------|
| Icons | 크기 ≤ 64px, 또는 이름에 icon/ic/arrow/chevron/close/check 포함 |
| Images | content 영역 내 이미지, 일러스트, 사진 |
| Backgrounds | 프레임/섹션 전체를 덮는 이미지, 이름에 bg/background 포함 |
| Buttons | 버튼 내부 이미지, 이름에 btn/button 포함 |
| Misc | 위에 해당하지 않는 에셋 |

### 에셋 레지스트리 (중복 방지)

에셋 URL → Unity 로컬 경로 매핑을 내부적으로 관리한다:

```
assetRegistry = {
  "https://figma.com/api/mcp/asset/abc123": "Assets/FigmaAssets/Icons/icon_back.png",
  "https://figma.com/api/mcp/asset/def456": "Assets/FigmaAssets/Images/hero.png",
}
```

**규칙:**
- 같은 URL은 한 번만 다운로드한다
- 여러 UI 요소가 같은 에셋을 참조하면 동일한 `spritePath`를 재사용한다
- 파일이 이미 디스크에 존재하면 다운로드를 건너뛴다
- `asset.import-texture`도 이미 Sprite 타입이면 재임포트를 건너뛴다

### 상세 단계

1. `get_design_context` 응답에서 에셋 URL 식별:
   - 코드 내 `const image = 'https://www.figma.com/api/mcp/asset/...'`
   - `<img src={image} />` 패턴에서 URL 추출
   - 모든 URL을 수집하고 중복을 제거한다

2. 분류 후 다운로드 (이미 존재하는 파일은 건너뜀):
   ```bash
   mkdir -p {projectPath}/Assets/FigmaAssets/{Icons,Images,Backgrounds,Buttons,Misc}

   # 파일이 없을 때만 다운로드
   [ ! -f {path} ] && curl -o {path} {url}
   ```

3. Unity에 에셋 등록 (모든 다운로드 완료 후 1회):
   ```bash
   unity-cli editor refresh
   ```

4. **Sprite 임포트 설정 (새로 다운로드한 에셋만)**:
   ```bash
   unity-cli asset import-texture \
     path=Assets/FigmaAssets/Icons/icon_back.png \
     textureType=Sprite

   unity-cli asset import-texture \
     path=Assets/FigmaAssets/Images/hero.png \
     textureType=Sprite maxTextureSize=2048
   ```

5. Image 생성 시 에셋 할당 (레지스트리에서 경로 참조):
   ```bash
   unity-cli ui image.create canvasName=UICanvas name=HeroImage \
     parentName=Content spritePath=Assets/FigmaAssets/Images/hero.png \
     preserveAspect=true size=400,300

   # 같은 아이콘을 여러 곳에서 재사용
   unity-cli ui image.create ... spritePath=Assets/FigmaAssets/Icons/icon_back.png ...
   unity-cli ui image.create ... spritePath=Assets/FigmaAssets/Icons/icon_back.png ...
   ```

### 이미지 파라미터

| 파라미터 | 설명 | 기본값 |
|---------|------|--------|
| `spritePath` | Unity 프로젝트 내 에셋 경로 | (필수) |
| `preserveAspect` | 원본 비율 유지 | false |
| `useNativeSize` | 스프라이트 원본 해상도로 크기 설정 | false |
| `imageType` | Simple / Sliced / Tiled / Filled | Simple |
| `color` | 틴트 색상 (hex) | #FFFFFFFF |

### 자동 임포트 폴백

`ui.image.create`에서 `spritePath` 로드 시 Sprite가 null이면 자동으로:
1. TextureImporter 확인
2. textureType != Sprite이면 Sprite로 변경 + SaveAndReimport
3. 다시 LoadAssetAtPath<Sprite> 시도

따라서 `asset.import-texture`를 명시적으로 호출하지 않아도 동작하지만,
에셋이 많은 경우 배치 처리를 위해 명시적 호출을 권장한다.

## 복잡한 구조 처리

응답이 너무 큰 경우:
1. `get_metadata`로 최상위 구조 파악
2. 주요 섹션(Header, Content, Footer 등)의 nodeId 식별
3. 섹션별로 `get_design_context` 개별 호출
4. 결과를 합쳐서 전체 UI 트리 구성
