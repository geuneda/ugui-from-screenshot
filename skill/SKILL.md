---
name: ugui-from-screenshot
description: UI 디자인 스크린샷을 기반으로 unity-cli를 사용하여 UGUI를 자동 구성하는 스킬. 스크린샷 분석, Canvas 설정, UI 요소 생성, 반복 검증(90%+ 일치), 다해상도 대응 점검을 수행한다. 'UGUI from screenshot', '스크린샷으로 UI', 'UI 구현', 'screenshot to UGUI', '스크린샷 기반 UI' 등의 요청 시 트리거된다.
---

# UGUI from Screenshot

UI 디자인 스크린샷을 분석하여 unity-cli로 UGUI를 자동 구성하고, 반복 검증-수정 루프를 통해 90%+ 일치를 달성하는 스킬.

## Prerequisites

- unity-cli 설치 및 Bridge 연결 상태 (브릿지 개선 버전 필요)
- Unity Editor에 unity-connector 패키지 설치
- 브릿지 상태 확인: `unity-cli status`

## Phase 1: Setup & Interview

### Step 1.1: 스크린샷 수신

사용자로부터 레퍼런스 스크린샷 파일 경로를 받는다.
Read 도구로 이미지를 로드하여 비전 분석을 수행한다.

### Step 1.2: 정보 수집

다음 정보를 확인한다. 사용자가 제공하지 않은 항목은 질문한다:

| 항목 | 필수 | 기본값 | 예시 |
|------|------|--------|------|
| 레퍼런스 스크린샷 | O | - | /path/to/screenshot.png |
| 기준 해상도 | O | - | 1440x3040 |
| ScreenMatchMode | X | Expand | Expand, Shrink, MatchWidthOrHeight |
| matchWidthOrHeight | X | 0.5 | 0~1 사이 값 |
| 타겟 방향 | X | Portrait | Portrait, Landscape |

질문 형식:
```
스크린샷을 분석했습니다. 다음 정보를 확인해주세요:
1. 기준 해상도가 무엇인가요? (예: 1440x3040)
2. ScreenMatchMode는 Expand(기본)로 설정할까요?
```

### Step 1.3: Canvas 생성

```bash
unity-cli ui canvas.create name=UICanvas \
  referenceResolution={width},{height} \
  screenMatchMode={mode}
```

## Phase 2: Analysis & Build

### Step 2.1: 스크린샷 분석

Claude 비전으로 스크린샷을 상세 분석한다:

1. **UI 요소 식별**: 각 요소의 타입, 위치, 크기, 색상, 텍스트
2. **계층 구조 파악**: 헤더/콘텐츠/하단바 등 영역 구분
3. **앵커링 전략**: `references/anchoring-strategy.md` 참조하여 결정
4. **레이아웃 패턴**: 반복 요소 -> Layout Group, 고정 바 -> 스트레치 앵커

분석 결과를 트리 구조로 정리:
```
UICanvas (referenceResolution)
  Header (top-anchored, full-width)
    BackButton (left)
    TitleText (center)
    MenuButton (right)
  Content (stretch between header/footer)
    ScrollView
      ...
  BottomBar (bottom-anchored, full-width)
    Tab1, Tab2, Tab3
```

### Step 2.2: UI 빌드

부모 -> 자식 순서로 생성. 접두사 규칙: 이름 충돌 방지를 위해 의미있는 고유 이름 사용.

```bash
# 1. 컨테이너 패널
unity-cli ui panel.create canvasName=UICanvas name=Header \
  anchorMin=0,1 anchorMax=1,1 pivot=0.5,1 \
  anchoredPosition=0,0 size=0,{headerHeight} color={headerColor}

# 2. 자식 요소
unity-cli ui text.create canvasName=UICanvas name=HeaderTitle \
  parentName=Header text="{title}" \
  fontSize={size} fontStyle=Bold alignment=MiddleCenter \
  color={textColor}

# 3. Layout 추가
unity-cli ui layout.add name=Header layoutType=Horizontal \
  spacing=16 childAlignment=MiddleCenter \
  paddingLeft=24 paddingRight=24
```

### Step 2.3: 리소스 매칭

스크린샷에서 아이콘/이미지가 있는 경우:
1. 프로젝트 Assets에서 유사한 스프라이트 검색 (Glob 도구 사용)
2. 매칭되는 에셋이 있으면 spritePath로 할당
3. 없으면 placeholder Image(색상만)로 생성하고 사용자에게 안내

## Phase 3: Verification Loop

최대 5회 반복. 90%+ 일치 시 PASS.

### Step 3.1: 캡처

```bash
unity-cli editor gameview.resize width={refWidth} height={refHeight}
unity-cli ui screenshot.capture width={refWidth} height={refHeight} \
  outputPath=Assets/Screenshots/verify_{n}.png
```

### Step 3.2: 비교

Read 도구로 레퍼런스와 캡처 스크린샷을 동시에 로드하여 비교.

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
# 위치 수정
unity-cli ui recttransform.modify name=Header anchoredPosition=0,-10 size=0,200

# 색상 수정
unity-cli component update name=Header type=UnityEngine.UI.Image \
  values={"color":"#1A1A2EFF"}

# 텍스트 수정
unity-cli component update name=HeaderTitle type=TMPro.TextMeshProUGUI \
  values={"fontSize":52,"text":"Corrected Title"}

# Layout 조정
unity-cli ui layout.add name=Content layoutType=Vertical spacing=20
```

### Step 3.4: 반복 판정

- 90%+ -> PASS, Phase 4로 진행
- 5회 초과 -> 현재 상태 보고, 남은 이슈 목록화, 사용자 판단 요청

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

- 모든 해상도 통과 -> Phase 5
- 3회 초과 미통과 -> 해당 해상도 이슈 보고, 사용자 판단 요청

## Phase 5: Complete

1. Game View 기준 해상도 복원:
   ```bash
   unity-cli editor gameview.resize width={refWidth} height={refHeight}
   ```

2. 결과 보고:
   ```
   ## UGUI 구성 완료

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
