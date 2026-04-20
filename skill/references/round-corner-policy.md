# 라운드 코너 처리 정책 (전역 규칙)

본 스킬은 **라운드 코너(rounded corner) / 원형 / 알약(pill) 형태에 대한 자동 보정/생성 시도를 수행하지 않는다.**
이 결정은 의도된 것이며, 어떤 Phase 에서도 이 규칙을 깨지 말아야 한다.

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

따라서 본 스킬은 **라운드 처리는 사용자가 직접 한다**는 정책을 채택한다.

## 적용 대상

이 정책은 다음 세 경우 모두에 적용된다:

1. **Path A (Figma URL → UnityToFigma 일괄 임포트)**
   - UnityToFigma 가 SDF Rectangle/Ellipse 로 라운드를 잘 처리한 경우 → **그대로 둔다.** 추가 보정 시도 금지.
   - UnityToFigma 가 라운드를 표현하지 못한 경우 → **사용자에게 보고만** 한다. 보정 단계에서 sprite/mask 시도 금지.
2. **Path B (스크린샷 입력)**
   - 비전 분석 단계에서 라운드 코너로 보이는 요소 → **각진 사각형 placeholder 로 대체** + 라운드 미처리 항목으로 리포트.
3. **검증 루프 (Phase 3) / 다해상도 (Phase 4)**
   - 캡처 비교에서 라운드 mismatch 가 발견되어도 **수정 명령을 보내지 않는다.** "라운드 미처리" 항목으로만 누적.

## 식별 방법

다음 패턴이 보이면 라운드 후보로 분류한다:

- 노드 이름에 `round`, `rounded`, `pill`, `circle`, `_r`, `Btn` 등이 포함
- Figma `cornerRadius` > 0 (Rectangle), `arcData` 가 있는 Ellipse
- 스크린샷에서 모서리가 직각이 아닌 모든 사각형/원

부트스트랩 리포트(`assets/UnityToFigmaBootstrap.cs`)의 `roundedSkipped` 필드에 자동 누적된다.

## 보고 양식 (Phase 5 완료 보고)

```
### 라운드 처리 미수행 항목 (사용자 수동 작업 필요)
- prefab: Assets/Figma/Screens/MainScreen.prefab
  path: Canvas/MainScreen/Header/RoundedAvatar
  reason: name-marker (avatar 둥근 처리 추정)
  추천: 둥근 마스크 / SDF / 별도 sprite 직접 적용
- prefab: ...
```

## 사용자 안내 문구 (Phase 5 마지막에 출력)

```
라운드 코너 / 원형 처리는 본 스킬에서 자동 적용하지 않습니다.
위 N 개 항목은 디자인 의도(반경, 두께, anti-aliasing) 보존을 위해
직접 다음 중 하나로 처리해 주세요:

  1) 디자인 시스템에서 9-slice 라운드 sprite 를 만들어 Image.sprite 에 할당
  2) UnityToFigma SDF Rectangle 의 cornerRadius 를 인스펙터에서 직접 입력
  3) UI Effect 패키지(Outline/Round) 등 프로젝트 표준 컴포넌트 사용
```

## 위반 시 회수 절차

만약 보정 단계에서 누군가 라운드 sprite 를 강제 할당했다면:

1. 해당 변경을 즉시 되돌린다 (`git diff`/`Editor undo`).
2. 본 문서를 다시 읽는다.
3. 사용자 보고서에만 기록하고 끝낸다.
