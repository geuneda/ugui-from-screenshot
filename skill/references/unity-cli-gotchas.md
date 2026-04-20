# Unity-CLI Gotchas (실제 브릿지 버전 차이)

실제로 겪은 시행착오 기반으로 unity-cli 사용 시 반드시 숙지할 내용.

**원칙**: SKILL.md에서 언급한 명령이라도 현 브릿지에 없을 수 있다. 작업 시작 전 `unity-cli tool list`로 실제 지원되는 명령을 반드시 확인한다.

---

## 1. 존재하지 않는 명령 (브릿지 구버전)

아래 명령들은 SKILL.md에 적혀있지만 **실제 브릿지에는 없을 수 있다**. 사전 확인 필수.

| 명령 | 상태 | 대체 방법 |
|------|------|-----------|
| `ui panel.create` | 없음 | `ui image.create color=#00000000` (투명 이미지로 컨테이너) |
| `ui recttransform.modify` | 없음 | `component update type=UnityEngine.RectTransform values=...` |
| `ui layout.add` | 없음 | `component update type=UnityEngine.UI.VerticalLayoutGroup values=...` (동적 추가는 Editor 스크립트 필요) |
| `ui screenshot.capture` | 없음 | `assets/GameViewCapture.cs` Editor 스크립트 필요 |
| `editor gameview.resize` | 없음 | GameView를 수동으로 설정하거나 capture 스크립트에서 RT 크기 지정 |

**확인 명령**:
```bash
unity-cli tool list | grep -E "(panel|layout|recttransform|screenshot|gameview)"
```

출력이 없으면 해당 명령이 없는 것이다.

---

## 2. 부모 지정 (parentName/parentId 무시됨)

가장 고통스러운 버그. `ui.*.create`의 `parentName`과 `parentId` 파라미터는 **조용히 무시되고** 항상 Canvas 루트 아래에 생성된다.

**반드시 3단계 플로우로 처리**:

```bash
# 1. 요소 생성 (Canvas 아래 생성됨)
unity-cli ui image.create canvasName=UICanvas name=Header color=#00000000 ...

# 2. parentId로 reparent (worldPositionStays=false 필수)
#    ※ parentName은 reparent에서도 동작하지 않음. parentId(InstanceID)만 사용
unity-cli gameobject reparent name=Header parentId=<부모_InstanceID> worldPositionStays=false

# 3. RectTransform 재설정 (reparent가 anchoredPosition/sizeDelta를 망가뜨림)
unity-cli component update name=Header type=UnityEngine.RectTransform \
  values='{"anchorMin":{"x":0,"y":1},"anchorMax":{"x":0,"y":1},"pivot":{"x":0,"y":1},"anchoredPosition":{"x":32,"y":-32},"sizeDelta":{"x":918,"y":77}}'
```

**중요**:
- `gameobject reparent`에 `parentName`, `newParent`를 전달하면 응답은 성공이지만 실제로는 scene root(parentId=0)로 이동한다
- InstanceID는 create 응답의 `result.id`에 들어있다 (음수)
- `worldPositionStays=false`는 **필수**. 생략하면 위치/스케일이 보존되려다 예상치 못한 값이 된다

**트래킹**: create 시 반환된 `result.id`를 변수에 저장하고, 자식 요소의 parentId로 사용한다.

---

## 3. 텍스트 파라미터의 쉘 이스케이프 함정

다음은 **버그 발생**:

```bash
# 이렇게 쓰면 text는 "MCP"만 전달됨 (Import, Smoke는 positional args로 버려짐)
unity-cli ui text.create text="MCP Import Smoke" fontSize=40
```

Bash가 `text=MCP` `Import` `Smoke`로 분리한다. 따옴표 보존은 원자적 argv 전달이 보장되지 않는다.

**해결 방법 A (권장)**: Python에서 JSON으로 업데이트

```python
# scripts/batch_set_texts.py 패턴
import subprocess, json

values = json.dumps({"text": "MCP Import Smoke"})
subprocess.run(["unity-cli", "component", "update",
    "name=HeadingTitle",
    "type=TMPro.TextMeshProUGUI",
    f"values={values}"])
```

**해결 방법 B**: 생성 시엔 임시 텍스트 "X", 생성 후 Python으로 실제 텍스트 업데이트

**검증**: 텍스트 설정 후 반드시 hierarchy 조회로 실제 값 확인
```bash
unity-cli resource get ui/hierarchy | python3 -c "import json,sys; d=json.load(sys.stdin); [print(i['name'], repr(i.get('text'))) for i in d['data']['items'] if i.get('text')]"
```

---

## 4. TMP 컴포넌트 속성명

`component update type=TMPro.TextMeshProUGUI values=...`에 전달할 속성:

| 원하는 동작 | 올바른 키 | 잘못된 키 (무시됨) |
|-------------|----------|------------------|
| 텍스트 내용 | `text` | `m_text` |
| 줄바꿈 허용 | `textWrappingMode: 1` (Normal) | `enableWordWrapping: true` (구버전) |
| 오버플로우 모드 | `overflowMode: 0` (Overflow) | `m_overflowMode` |
| 폰트 크기 | `fontSize` | - |

**기본값 문제**: TMP 기본 `overflowMode`는 `Ellipsis`(1). 즉, 긴 텍스트가 자동으로 "..."로 잘린다. 이를 방지하려면 반드시 `Overflow`(0)로 변경.

**단순 설정만으로는 mesh가 리빌드 안 되는 경우**: Editor 스크립트로 `ForceMeshUpdate()` 호출 필요. `assets/FixTMPSettings.cs` 참조.

---

## 5. 기본 UI Sprite (UISprite) 둥근 모서리 문제

`ui image.create`로 만든 Image는 `sprite=null`로 생성되어도 Unity 기본 UISprite(둥근 모서리)가 적용될 수 있다. 각진 사각형을 원하면 명시적으로 평면 sprite를 할당해야 한다.

**해결**: 프로젝트에 이미 존재하는 플랫 화이트 스프라이트(예: `Placeholders/HpBarWhite.png`)를 사용하도록 Editor 스크립트로 일괄 교체한다.

`assets/ReplaceUISprites.cs` 템플릿 참조. 메뉴에서 실행:
```bash
unity-cli menu execute path="Tools/Replace UI Images With Flat Sprite"
```

---

## 6. 캡처 (Screenshot) 구현

`ui screenshot.capture`는 없다. 자체 Editor 스크립트로 처리한다.

**주의사항**:
- **Overlay Canvas는 카메라로 렌더되지 않음** → ScreenSpaceCamera로 일시 전환 필요
- **CanvasScaler**가 활성 상태면 Screen.width/height와 RT 크기 불일치로 UI가 축소됨 → 캡처 직전 `ScaleMode.ConstantPixelSize, scaleFactor=1`로 임시 변경, 캡처 후 원복
- **GameView RT를 직접 읽을 때** 결과가 **상하 반전**되어 있음 → 픽셀 배열 플립 필요
- 캡처 후 반드시 **원복** (Overlay 모드, CanvasScaler 설정) → 실패 시 씬이 망가진 상태로 저장됨

`assets/GameViewCapture.cs` 템플릿 참조 (ScreenSpaceCamera + ConstantPixelSize 방식, 가장 안정적).

---

## 7. 좌표 변환: Figma → UGUI

Figma는 Y축 아래 방향(top-down), Unity UI는 Y축 위 방향(bottom-up).

**가장 단순한 매핑** (복잡한 앵커 전략 대신):
- **자식 요소**: `anchor=(0,1)`, `pivot=(0,1)` (부모 좌상단 기준)
- `anchoredPosition = (figma.x, -figma.y)` (Y 부호 반전)
- `sizeDelta = (figma.w, figma.h)`

이 방식은 반응형은 아니지만 **Figma 좌표를 그대로 쓸 수 있어** 시행착오가 없다. 반응형 대응이 꼭 필요한 경우에만 `references/anchoring-strategy.md`를 참고해서 개별 적용.

---

## 8. Canvas Scaler와 프로젝트 기준 해상도

Figma 프레임의 해상도와 프로젝트의 기준 해상도가 다를 수 있다.

- Figma 프레임: 디자이너의 작업 해상도 (예: 1440x3040)
- 프로젝트: `CLAUDE.md` 또는 기존 Canvas의 `referenceResolution` (예: 1440x3040)

**정책**:
- 두 값이 같으면 Figma 좌표를 그대로 사용
- 다르면 Figma 좌표를 프로젝트 해상도에 맞게 **스케일링**하거나, 사용자에게 확인 (이 스킬은 기본적으로 **프로젝트 기준 해상도 우선**)

---

## 9. 브릿지 재시작 (Compile 중)

Editor 스크립트 추가/수정 → `unity-cli editor compile` 실행 시 **브릿지가 일시적으로 끊겨** `Connection refused` 발생.

**대응**:
```bash
unity-cli editor compile && sleep 3 && unity-cli status
```

재연결을 확인한 뒤 다음 명령 수행.

---

## 10. 체크리스트 (매 작업 시작 전)

- [ ] `unity-cli status` → bridge ready 확인
- [ ] `unity-cli tool list` → 지원 명령 확인
- [ ] `unity-cli resource get ui/hierarchy` → 기존 UI 상태 파악
- [ ] 새 씬에서 작업 vs 기존 씬 수정 여부 결정 (기존 BootScene 등 중요 씬은 건드리지 말 것)
- [ ] `/tmp/ui_helper.sh`와 `scripts/batch_set_texts.py`를 스킬 디렉토리에서 복사해 준비
- [ ] 평면 스프라이트 에셋 경로 확인 (`find Assets -name "*White*.png"`)
