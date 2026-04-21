# Figma 1차 정리 정책 (Path A 진입 전 위생 점검)

UnityToFigma 가 Figma 문서를 Unity UGUI 로 일괄 임포트하기 전에, **에이전트가 Figma MCP 로 1차 정리**를 수행해야 한다.
정리되지 않은 문서를 그대로 sync 하면 다음 문제가 발생한다:

- 화면 단위 Frame 이 없으면 Screen prefab 이 의도치 않은 단위로 잘리거나 만들어지지 않음
- 한국어/특수문자 레이어명이 GameObject 이름에 그대로 박혀 코드 참조/검색이 어렵고, 일부 환경에서 인코딩 이슈 유발
- "레이어 1 복사 4", "사각형 19 복사 3", "그룹 5", "타원 4 복사" 같은 자동 생성 이름은 Unity 에서 의미 없는 GameObject 이름이 되어 유지보수가 어려워짐
- 같은 이름이 여러 개면 GameObject 이름 충돌 → 자동 suffix 가 붙어 코드 참조가 불안정

따라서 **sync 전 다음 4가지를 반드시 점검·정리한다**.

---

## 1) 화면 루트 Frame 확보 (필수)

### 점검 기준
- `figma.currentPage.children` 의 직접 자식 중 **화면 전체를 감싸는 단일 Frame** 이 있는가?
- 없거나, 화면 요소들이 Page 의 직접 자식으로 평면 나열돼 있다면 **반드시 Frame 으로 감싸야 한다**.

### 화면 사이즈 추정 절차 (중요)

평면 배치된 페이지에서 MainScreen 의 적절한 W x H 를 정하는 우선순위:

1. **가장 큰 background rectangle 의 사이즈** 를 그대로 사용한다.
   - 디자이너는 거의 항상 화면 전체를 덮는 배경 사각형 (`사각형 1`, `Background`, `BG` 등) 을 둔다.
   - 이 노드의 `width x height` 가 곧 디자인 해상도다 (예: `1080x1920`, `1440x3040`).
2. background 후보가 모호하면 **모든 직접 자식의 bbox 를 합친 영역** 을 사용. 단 음수 좌표 (블리드) 는 0 으로 클램프.
3. 둘 다 안 되면 디자인 컨텍스트로 추론 (모바일 세로면 `1080x1920`, 태블릿이면 `1440x2160` 등) 후 사용자에게 명시.

추정한 사이즈를 `main.resize(W, H)` 인자로 그대로 쓴다. **이 W x H 는 Unity 의 `CanvasScaler.referenceResolution` 으로 자동 사용** 되므로 다해상도 대응의 기준이 된다.

### 화면 이름 결정 (중복 회피)

다른 페이지에 동일 이름의 Frame 이 이미 있으면 UnityToFigma 가 자동으로 `_1`, `_2` suffix 를 붙여 prefab 을 저장한다 (예: `MainScreen.prefab`, `MainScreen_1.prefab`).

부트스트랩이 suffix 후보를 자동 폴백으로 매칭하긴 하지만, 가능하면 **화면 컨텍스트 prefix** 를 붙여 충돌을 미리 피한다:

| 상황 | 권장 이름 |
|------|----------|
| 단일 화면 디자인 | `MainScreen` |
| 로비/메인 메뉴 | `LobbyScreen`, `MainMenuScreen` |
| 설정 화면 | `SettingsScreen` |
| 상점 | `ShopScreen` |
| 전투 HUD | `BattleHud` |
| 결과/리워드 | `RewardPopup`, `ResultScreen` |

### 정리 절차 (use_figma 한 번 호출로 처리)

```javascript
const page = figma.currentPage;
let main = page.children.find(n => n.name === 'MainScreen' && n.type === 'FRAME');
if (!main) {
  // 1) 사이즈 추정: 가장 큰 background rectangle 우선
  const bg = page.children
    .filter(n => n.type === 'RECTANGLE')
    .sort((a, b) => (b.width * b.height) - (a.width * a.height))[0];
  const W = bg ? Math.round(bg.width)  : 1080;
  const H = bg ? Math.round(bg.height) : 1920;

  main = figma.createFrame();
  main.name = 'MainScreen'; // 또는 화면 컨텍스트 (LobbyScreen, SettingsScreen 등)
  main.x = bg ? bg.x : 0;
  main.y = bg ? bg.y : 0;
  main.resize(W, H);
  main.fills = []; // 자식 배경이 따로 있으면 투명, 아니면 디자인 배경색 채움
  main.clipsContent = true;
  page.appendChild(main);

  // 2) 모든 기존 직접 자식을 MainScreen 안으로 reparent (좌표 자동 보존)
  const others = page.children.filter(c => c !== main).slice();
  for (const node of others) main.appendChild(node);
}
```

### 왜 필수인가
- UnityToFigma 는 **최상위 Frame 단위로 Screen prefab 을 만든다**. Page 직접 자식들만 있으면 어떤 단위로 prefab 이 만들어질지 예측 불가능하고, 화면 통째로 하나의 prefab 으로 안 나올 수 있음.
- Unity 측 부트스트랩이 `RectTransform.sizeDelta` 를 읽어 Canvas `referenceResolution` 을 자동 설정한다 (`UnityToFigmaBootstrap.InstantiateDefaultScreen`). 화면 Frame 이 없으면 referenceResolution 추정이 깨진다.

---

## 2) 한국어/자동생성 레이어명 → 의미있는 영문명 (강력 권장)

### 점검 기준
- 한글, 일본어, 특수문자, 공백이 들어간 레이어 이름
- "레이어 N", "사각형 N", "타원 N", "그룹 N", "복사", "복사 2" 등 Figma 자동 생성 이름
- 같은 이름이 여러 군데 등장 (단순 GameObject 이름으론 식별 불가)

### 명명 규칙
- **PascalCase 영문**. 짧고 의미 있는 단어 1~3개.
- **역할 기반**: `Background`, `BottomTabBar`, `BattleStartButton`, `EnergyResource`, `DiamondCountText`, `LobbyTab`, `LobbyTabBg`, `LobbyTabIcon`, `LobbyTabLabel`
- **계층 정보 포함**: 상단/하단/좌/우 그룹은 `TopHud`, `BottomTabBar`. 자식들은 부모 prefix 를 붙여 `BottomTabBar/ShopTab/ShopTabIcon` 처럼 자기 위치를 알게 함.
- **숫자 인덱스 금지**: `Layer1Copy4`, `Rect2`, `Group5` 같은 의미 없는 이름은 절대 두지 않는다. 정 모르겠으면 형태 + 역할 추정 (`UnknownDecorRect_18` 처럼 표시해 사람이 후속 정리할 수 있게).
- **중복 회피**: "Energy" 가 두 군데에 있으면 `EnergyResource`(상단 HUD) / `EnergyCostIcon`(시작 버튼) 처럼 컨텍스트로 구분.

### 정리 절차

`get_metadata` 로 모든 노드 ID + 이름을 받은 뒤, ID → 새 이름 매핑 테이블을 만들고 한 번의 `use_figma` 호출로 일괄 변경.

```javascript
const renameMap = {
  '3:3': 'Background',
  '3:7': 'BottomTabBar',
  '3:8': 'ShopTab',
  '3:9': 'ShopTabBg',
  '3:10': 'ShopTabIcon',
  // ... 모든 한글/자동생성 이름을 매핑
};
let renamed = 0;
for (const id of Object.keys(renameMap)) {
  const node = await figma.getNodeByIdAsync(id);
  if (node) { node.name = renameMap[id]; renamed++; }
}
return { renamed };
```

### 텍스트 *내용* 은 건드리지 않는다
레이어 *이름* 만 영문화한다. 텍스트 노드의 `characters` (실제 게임 문구: "전투 시작", "퀘스트", "벙커디펜스닉네임" 등) 는 디자인 의도이므로 그대로 둔다.
한글 글리프 자체는 Unity 폰트 fallback 에 한글이 포함된 폰트 (예: NotoSansKR) 를 등록해 해결한다 (`references/font-fallback-policy.md`).

---

## 3) 부모-자식 관계 정리

### 점검 기준
- 같은 시각적 그룹인데 서로 다른 부모에 흩어진 노드들
- 의미상 한 컴포넌트인데 형제 관계로 평면화되어 있는 노드들 (배경 사각형 + 라벨이 한 그룹이 아닌 채 평면 나열)
- 화면 영역을 벗어난 좌표의 노드 (예: x=-7) — 의도적인 그래픽 효과(블리드)면 두지만, 실수면 정리

### 정리 절차
Auto Layout 을 강제하지는 않는다 (UnityToFigma 가 Auto Layout 도 정상 처리하지만, 안 쓴 디자인을 임의로 바꾸면 의도된 픽셀 정렬이 깨질 수 있음).
대신 **시각적으로 한 단위인 노드들은 같은 Frame 으로 묶기**만 수행한다.
좌표는 reparent 시 Figma 가 자동 유지하므로 안전.

---

## 4) 사후 검증

`use_figma` 응답에서 다음을 확인한다:

```json
{
  "mainScreenId": "<생성된 MainScreen 의 ID>",
  "mainChildCount": <0 보다 커야 함>,
  "renamed": <매핑한 개수와 일치해야 함>,
  "missing": []
}
```

`missing` 이 비어있지 않으면 매핑 ID 가 존재하지 않는다는 뜻이므로 (다른 페이지에 있거나 ID 가 잘못된 경우), 해당 노드들을 다시 조회해서 매핑을 보정한다.

---

## 자동화 체크리스트 (에이전트가 매번 수행)

1. `get_metadata` 로 대상 노드 트리 가져오기
2. 화면 루트 Frame 존재 여부 확인 → 없으면 MainScreen Frame 으로 감싸기
3. 모든 노드 이름 스캔 → 한글/자동생성 이름 발견 시 매핑 테이블 작성
4. `use_figma` 한 번에 wrap + rename 실행
5. 응답의 `missing`/`renamed`/`mainChildCount` 검증
6. URL 의 `node-id` 를 새로 만든 MainScreen 의 ID 로 갱신해서 sync 호출
   - 예: `?node-id=4-2` (MainScreen ID = `4:2` → URL 표기 `4-2`)

이 단계를 거친 뒤에만 UnityToFigma sync 를 실행한다. 정리되지 않은 문서로 sync 하면 후속 보정 작업량이 폭증한다.
