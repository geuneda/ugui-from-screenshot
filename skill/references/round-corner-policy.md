# 라운드 코너 처리 정책 (전역 규칙)

본 스킬은 **라운드 코너(rounded corner) / 원형 / 알약(pill) 형태에 대한 자동 보정/생성/스프라이트 대체 시도를 수행하지 않는다.**
이 결정은 의도된 것이며, 어떤 Phase 에서도 이 규칙을 깨지 말아야 한다.

다만 Path A 와 Path B 의 처리 의미가 다르므로 명확히 구분한다.

## 배경

이전 버전의 스킬은 라운드 처리된 디자인 요소를 자동 재현하기 위해 다음을 시도했다:

- 1x1 흰색 스프라이트 + Mask 컴포넌트로 모서리 흉내
- 9-slice sprite 검색해서 Image 에 강제 할당
- ScreenSpace mesh 분석으로 corner radius 추정 후 SDF 흉내

이 시도들은 거의 항상 다음 문제를 일으켰다:

- 비율이 어긋난 라운드 (특히 스트레치 앵커에서 모서리 반지름이 늘어남)
- 잘못된 sprite 매칭 (이름만 비슷한 다른 모서리 반지름 sprite 가 적용됨)
- Mask 사용으로 인한 드로우콜 폭증 / 자식 UI 파괴
- 사용자가 의도한 "디자인 토큰 라운드" 와 실제 결과의 비주얼 mismatch

따라서 본 스킬은 **라운드 코너의 자동 합성/대체를 금지**한다.

## 핵심 사실 (Path A 한정)

UnityToFigma 패키지의 `UnityToFigma.Runtime.UI.FigmaImage` 컴포넌트는
**SDF 기반 cornerRadius 를 자체 처리한다.** 즉:

- 모든 코너 반지름이 동일/다른 모든 케이스 (`{x,y,z,w}`) 를 그대로 보존
- pill / circle 은 `{999, 999, 999, 999}` 등 큰 값으로 표현되며 SDF 가 자연스럽게 알약 모양을 만든다
- Stroke / StrokeWidth 도 SDF 기반으로 정확하게 그려짐

따라서 **Path A 에서는 "라운드를 못 가져온다" 가 사실이 아니다.** UnityToFigma 가 가져온 라운드는 정확하다.

이 정책의 "자동 보정 금지" 는 **그 위에 무언가를 추가/대체하지 않겠다** 는 뜻이지,
"라운드 자체가 안 들어온다" 는 뜻이 아니다.

## Path 별 적용

### Path A (Figma URL → UnityToFigma 일괄 임포트)

- **라운드는 자동으로 잘 들어온다.** 추가 보정 시도 금지.
- 부트스트랩 리포트는 라운드 노드를 다음 세 카테고리로 분류한다:
  - `roundedHandled` : `cornerRadius != 0` 인 정상 처리 항목 (검수용 정보, 액션 불필요)
  - `roundedExtreme` : `max(cornerRadius) >= 500` (pill / circle 후보, **시각 검토 권장**)
  - `roundedSkipped` : FigmaImage 타입을 못 찾는 등 검출 실패 (실제로는 거의 발생하지 않음)
- Phase 3 검증 루프에서 라운드 mismatch 가 보여도 수정 명령을 보내지 않는다 (UnityToFigma 결과 신뢰).

### Path B (스크린샷 입력, 비전 분석)

- 비전 분석 단계에서 라운드 코너로 보이는 요소 → **각진 사각형 placeholder 로 대체** + 라운드 미처리 항목으로 리포트
- 스프라이트 강제 할당 / Mask 추가 / SDF 흉내 모두 금지
- 사용자에게 "이 N개 노드는 라운드로 추정되니 직접 처리하라" 만 보고

### 검증 루프 (Phase 3) / 다해상도 (Phase 4)

- 캡처 비교에서 라운드 mismatch 발견 시:
  - **Path A 는 무시** (UnityToFigma 가 정확히 처리했으므로 캡처 차이는 노이즈일 가능성 높음)
  - **Path B 는 "라운드 미처리" 카운트만 증가**, 점수에서 제외

## 식별 방법

### Path A: 부트스트랩 자동 분류
- `assets/UnityToFigmaBootstrap.cs` 의 `ScanRounded` 가 prefab 을 순회
- `FigmaImage.m_CornerRadius` (Vector4) 를 reflection 으로 읽음
- 위 세 카테고리로 자동 분류 → `Assets/_Temp/UnityToFigmaReport.json`

### Path B: 휴리스틱
- 스크린샷에서 모서리가 직각이 아닌 모든 사각형/원
- 노드 이름에 `round`, `rounded`, `pill`, `circle`, `_r`, `Btn` 포함

## 보고 양식 (Phase 5 완료 보고)

### Path A
```
### 라운드 코너 처리 결과 (UnityToFigma 자동 처리됨)
- 정상 처리: N 개 (cornerRadius 16/18/20/24/28 등)
- pill/circle 후보 (시각 검토 권장): M 개
  - prefab: Assets/Figma/Screens/Main.prefab
    path:  Frame/Section/Header/Container
    cornerRadius: [999, 999, 999, 999]
- 검출 실패: K 개 (대개 0)

이 항목들은 UnityToFigma SDF 가 자체 처리했으므로 추가 작업 불필요.
pill/circle 후보만 디자인 의도와 일치하는지 한 번 확인 권장.
```

### Path B
```
### 라운드 코너 미처리 항목 (사용자 수동 작업 필요)
- prefab: Assets/Generated/Screens/MainScreen.prefab
  path:  Canvas/MainScreen/Header/RoundedAvatar
  reason: 비전 분석에서 라운드 추정 (실제 처리는 위임)

추천 처리 방법:
  1) 디자인 시스템 9-slice 라운드 sprite 를 Image.sprite 에 할당
  2) UnityToFigma 로 한 번 임포트해서 FigmaImage 컴포넌트를 받기
  3) UI Effect 패키지(Outline/Round) 등 프로젝트 표준 컴포넌트 사용
```

## 위반 시 회수 절차

만약 보정 단계에서 누군가 라운드 sprite 를 강제 할당했거나 Mask 를 추가했다면:

1. 해당 변경을 즉시 되돌린다 (`git diff` / `Editor undo` / 프리팹 revert).
2. 본 문서를 다시 읽는다.
3. 사용자 보고서에만 기록하고 끝낸다.
