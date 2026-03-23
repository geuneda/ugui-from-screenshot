# Resolution Profiles

다해상도 검증을 위한 디바이스 프로파일.

## Target Devices

| ID | Device | Width | Height | PPI | Aspect Ratio | Orientation |
|----|--------|-------|--------|-----|-------------|-------------|
| ipad | iPad 10th gen | 2160 | 1620 | 265 | 4:3 | Landscape |
| iphone13 | iPhone 13 Pro | 1170 | 2532 | 457 | 19.5:9 | Portrait |
| galaxys21 | Galaxy S21 | 1080 | 2340 | 443 | 19.5:9 | Portrait |
| midandroid | Mid-range Android | 1080 | 2280 | 402 | 19:9 | Portrait |
| galaxys10 | Galaxy S10+ | 1440 | 3040 | 522 | 19:9 | Portrait |

## Aspect Ratio Groups

### Ultra-wide (19:9 ~ 19.5:9)
- iPhone 13 Pro, Galaxy S21, Mid-range Android, Galaxy S10+
- 세로로 긴 화면. 콘텐츠 영역이 넉넉하지만 가로 공간 제한.
- 주의: 가로 요소가 많으면 좁아질 수 있음.

### Standard (4:3)
- iPad
- 가로가 상대적으로 넓은 화면. 모바일 UI를 그대로 적용하면 좌우 여백 과다.
- 주의: 세로 전용 레이아웃은 iPad에서 비효율적일 수 있음.

## Validation Commands

### Full Validation Sequence

```bash
# 1. iPad (2160x1620)
unity-cli editor gameview.resize width=2160 height=1620
unity-cli ui screenshot.capture width=2160 height=1620 \
  outputPath=Assets/Screenshots/res_ipad.png

# 2. iPhone 13 Pro (1170x2532)
unity-cli editor gameview.resize width=1170 height=2532
unity-cli ui screenshot.capture width=1170 height=2532 \
  outputPath=Assets/Screenshots/res_iphone13.png

# 3. Galaxy S21 (1080x2340)
unity-cli editor gameview.resize width=1080 height=2340
unity-cli ui screenshot.capture width=1080 height=2340 \
  outputPath=Assets/Screenshots/res_galaxys21.png

# 4. Mid-range Android (1080x2280)
unity-cli editor gameview.resize width=1080 height=2280
unity-cli ui screenshot.capture width=1080 height=2280 \
  outputPath=Assets/Screenshots/res_midandroid.png

# 5. Galaxy S10+ (1440x3040)
unity-cli editor gameview.resize width=1440 height=3040
unity-cli ui screenshot.capture width=1440 height=3040 \
  outputPath=Assets/Screenshots/res_galaxys10.png
```

## Common Resolution Issues

### 4:3 vs 19:9 Transition

가장 큰 비율 차이. 주요 문제:

| Issue | Symptom | Fix |
|-------|---------|-----|
| 좌우 여백 과다 (iPad) | 콘텐츠가 중앙에 좁게 표시 | maxWidth 제한 + center anchor |
| 가로 요소 찌그러짐 | 버튼/카드가 너무 좁아짐 | Layout + 최소 너비 설정 |
| 세로 공간 부족 (iPad) | 스크롤 필요 영역 증가 | ScrollRect 활용 |

### High PPI vs Low PPI

| Issue | Symptom | Fix |
|-------|---------|-----|
| 텍스트 너무 작음 | 저PPI에서 가독성 저하 | CanvasScaler가 자동 처리 |
| 아이콘 흐림 | 고PPI에서 저해상도 스프라이트 | 고해상도 에셋 사용 |

### CanvasScaler ScreenMatchMode 영향

| Mode | 4:3 (iPad) | 19:9 (Phone) |
|------|-----------|--------------|
| Expand | UI가 작아짐 (더 많은 콘텐츠 표시) | 기준과 유사 |
| Shrink | 기준과 유사 | UI가 잘릴 수 있음 |
| MatchWidthOrHeight (0) | 가로 기준 스케일 | 세로 공간 부족 가능 |
| MatchWidthOrHeight (1) | 세로 기준 스케일 | 가로 공간 부족 가능 |
| MatchWidthOrHeight (0.5) | 중간 타협 | 중간 타협 |

### Expand Mode 권장 이유

- 모든 UI가 항상 화면 안에 표시됨 (잘림 없음)
- 여분 공간이 생길 수 있지만, 앵커링으로 대응 가능
- 가장 안전한 기본 선택

## Pass Criteria

각 해상도에서 다음 기준 모두 충족 시 PASS:

1. **No Clipping**: 모든 UI 요소가 화면 내에 완전히 표시
2. **No Overlap**: 비정상적 요소 겹침 없음
3. **Readable Text**: 모든 텍스트가 가독성 유지
4. **Maintained Proportion**: 요소 비율이 기준 대비 15% 이내
5. **Proper Spacing**: 여백/간격이 비정상적으로 확대/축소되지 않음
