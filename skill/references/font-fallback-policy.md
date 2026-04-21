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

1. **에이전트는 폰트 *파일* 자체를 자동 다운로드하지 않는다.**
   프로젝트마다 사용 가능한 한글 폰트 라이센스 / 디자인 가이드가 다르므로,
   라이센스 결정 (폰트 선택, 다운로드 동의) 은 사용자에게 위임한다.

2. **단, 사용자가 폰트 파일을 프로젝트에 두면 SDF 생성 + TMP fallback 등록은 자동화한다.**
   `Tools/UnityToFigma Bootstrap/Setup TMP Korean Fallback` 메뉴가 이걸 한 번에 처리한다 (검증됨, 2026-04-21).
   Dynamic Atlas Population 모드라 사전 글리프 등록도 불필요 (런타임 자동 생성).

3. **콘솔에 한글 fallback 워닝이 발견되면, sync 결과 리포트에 명시한다.**
   에이전트는 사용자에게 다음을 안내한다:
   - 어떤 텍스트 노드들이 영향받는지 (워닝의 `[NodeName]` 부분)
   - 권장 처리 방안 (아래 "사용자 처리 가이드 — 자동화")

4. **레이어 *이름* 은 영문화하되 텍스트 *내용* 은 건드리지 않는다.**
   디자인 의도를 보존해야 하므로 "전투 시작" 같은 게임 카피를 "Battle Start" 로 바꾸지 않는다.
   레이어 이름만 `BattleStartLabel` 처럼 의미 영문으로 정리한다 (`figma-prep-policy.md` 참고).

---

## 사용자 처리 가이드 — 자동화 (권장)

다음 순서로 단 두 단계만 거치면 한글이 정상 렌더링된다.

### 1단계: 한글 폰트 파일을 프로젝트에 배치

라이센스가 자유로운 옵션 중 하나 선택:

- **NotoSansKR** (Google Fonts, SIL OFL 1.1):
  ```bash
  mkdir -p <PROJECT>/Assets/Fonts
  curl -sSL -o "<PROJECT>/Assets/Fonts/NotoSansKR-Regular.ttf" \
    "https://github.com/google/fonts/raw/main/ofl/notosanskr/NotoSansKR%5Bwght%5D.ttf"
  ```
- **Pretendard** (SIL OFL 1.1): `https://github.com/orioncactus/pretendard`
- **나눔고딕** (SIL OFL 1.1): Google Fonts 등에서 다운로드

권장 경로: `Assets/Fonts/NotoSansKR-Regular.ttf` (자동 탐색 패턴에 일치).
Unity 가 임포트 완료할 때까지 잠시 대기.

### 2단계: 부트스트랩 메뉴 실행

```bash
unity-cli menu execute path="Tools/UnityToFigma Bootstrap/Setup TMP Korean Fallback"
```

기대 로그:

```
[UnityToFigmaBootstrap] Korean SDF 생성: Assets/Fonts/NotoSansKR-Regular_SDF.asset (Dynamic, source=NotoSansKR-Regular.ttf)
[UnityToFigmaBootstrap] TMP Korean Fallback 적용 완료. globalFallbackAdded=1, perAssetFallbackAdded=N
```

### 동작 상세

이 메뉴는 다음을 멱등하게 처리한다:

1. ContextFile `koreanFontPath` (또는 `UGUI_FIGMA_KOREAN_FONT_PATH` 환경변수) 우선, 없으면 자동 탐색:
   `Assets/Fonts/NotoSansKR*.ttf` → `Assets/Fonts/Noto*KR*.ttf` → `Pretendard*.ttf` → `*Korean*.ttf` → `*KR*.ttf` → `Nanum*.ttf` 순.
2. 같은 폴더에 `<FontName>_SDF.asset` 가 있으면 재사용, 없으면 `TMP_FontAsset.CreateFontAsset(font, 90, 9, SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true)` 로 Dynamic SDF 생성.
3. `TMP_Settings.fallbackFontAssets` (전역 fallback) 에 SDF 등록.
4. `Assets/Figma/Fonts/*.asset` (UnityToFigma 가 만든 SDF) 의 `m_FallbackFontAssetTable` 에도 SDF 등록 (per-asset).

폰트가 없으면 Debug.LogWarning 으로 안내만 하고 종료 (다이얼로그 없음 → 자동화 안전).

---

## 자동 안내 트리거

부트스트랩이 sync 후 콘솔 로그를 스캔하다가 다음 패턴이 N개 이상 발견되면 위 자동화 가이드를 콘솔에 출력한다 (선택적 후속 작업):

```
The character with Unicode value \\u[0-9A-F]+ was not found
```

지금은 부트스트랩이 워닝을 수집하지 않으므로 에이전트가 console get 으로 직접 확인 후 사용자에게 보고한다.
