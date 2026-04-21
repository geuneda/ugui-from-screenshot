# 한글/CJK 폰트 Fallback 정책

UnityToFigma 가 가져온 텍스트는 **TextMeshPro Essential Resources** 에 포함된 `LiberationSans SDF` 와 그 fallback 만으로 렌더된다.
이 폰트 세트에는 한글/일본어/중국어 글리프가 없어서, 한글 문자열이 들어간 TMP 텍스트는 모두 `□` (\u25A1) 로 표시된다.

콘솔에 다음과 같은 워닝이 수십~수백 줄 출력되면 이 문제다:

```
The character with Unicode value \uBA54 was not found in the
[LiberationSans SDF - Fallback] font asset or any potential fallbacks.
It was replaced by Unicode character \u25A1 in text object [LobbyTabLabel].
```

---

## 정책

1. **에이전트는 임의로 폰트를 추가하지 않는다.**
   프로젝트마다 사용 가능한 한글 폰트 라이센스 / 디자인 가이드가 다르므로,
   에이전트는 fallback 폰트를 깔거나 라이센스 파일을 만지지 않는다.

2. **콘솔에 한글 fallback 워닝이 발견되면, sync 결과 리포트에 명시한다.**
   `Assets/_Temp/UnityToFigmaReport.json` 와 함께, 에이전트는 사용자에게 다음을 안내한다:
   - 어떤 텍스트 노드들이 영향받는지 (워닝의 `[NodeName]` 부분)
   - 권장 처리 방안 (아래 "사용자 처리 가이드")

3. **레이어 *이름* 은 영문화하되 텍스트 *내용* 은 건드리지 않는다.**
   디자인 의도를 보존해야 하므로 "전투 시작" 같은 게임 카피를 "Battle Start" 로 바꾸지 않는다.
   레이어 이름만 `BattleStartLabel` 처럼 의미 영문으로 정리한다 (`figma-prep-policy.md` 참고).

---

## 사용자 처리 가이드 (안내 메시지로 출력)

```
한글 텍스트가 □ 로 표시됩니다. TextMeshPro 의 폰트 fallback 에 한글 글리프가 없어서입니다.

해결 방법 1 (권장): 한글 폰트를 TMP fallback 으로 추가
  1. 한글 TTF/OTF 를 Assets/Fonts/ 등에 임포트 (예: NotoSansKR-Regular.ttf)
  2. Window > TextMeshPro > Font Asset Creator
     - Source Font File: 임포트한 폰트
     - Sampling Point Size: 90 (Auto Sizing)
     - Padding: 9
     - Atlas Resolution: 4096 x 4096
     - Character Set: Unicode Range (Hex)
     - Character Sequence:
         3131-3163,AC00-D7A3,0020-007E,3000-303F,FF00-FFEF
     - "Generate Font Atlas" → "Save"
  3. 생성된 SDF 폰트를 LiberationSans SDF (또는 사용 중인 default font) 의
     Inspector > Fallback Font Assets 리스트에 추가

해결 방법 2: 영구적 해결을 원치 않으면, 디자인 텍스트를 영문으로 교체
  - Figma 측에서 텍스트를 영문으로 바꾸고 다시 sync
```

---

## 자동 안내 트리거

부트스트랩이 sync 후 콘솔 로그를 스캔하다가 다음 패턴이 N개 이상 발견되면 자동으로 위 안내 문구를 콘솔에 한 번 출력하도록 한다 (선택적 후속 작업):

```
The character with Unicode value \\u[0-9A-F]+ was not found
```

지금은 부트스트랩이 워닝을 수집하지 않으므로 에이전트가 console get 으로 직접 확인 후 사용자에게 보고한다.
