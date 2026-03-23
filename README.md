# UGUI from Screenshot

UI 디자인 스크린샷을 기반으로 Unity UGUI를 자동 구성하는 Claude Code 스킬 및 unity-cli 확장 구현 계획.

## Overview

이 프로젝트는 다음을 포함합니다:

1. **구현 계획서** (`PLAN.md`) - unity-cli 브릿지 개선 및 스킬 워크플로우 상세 계획
2. **Claude Code Skill** (`skill/`) - 스크린샷 기반 UGUI 자동 구성 스킬
3. **Bridge 변경 사양** (`bridge-patches/`) - UnityCliBridge.cs 확장 변경 목록

## How It Works

```
[Screenshot] --> [Claude Vision Analysis] --> [unity-cli Commands] --> [UGUI in Unity]
                                                     |
                                              [Screenshot Capture]
                                                     |
                                              [Visual Comparison]
                                                     |
                                           [< 90% match? Fix & Retry]
                                                     |
                                           [Multi-Resolution Check]
```

### Workflow

1. **Setup**: 레퍼런스 스크린샷 + 기준 해상도 입력
2. **Build**: Claude 비전으로 UI 요소 분석 후 unity-cli로 UGUI 구성
3. **Verify**: 스크린샷 비교 검증 루프 (90%+ 일치까지)
4. **Validate**: 5개 해상도 대응 검증 루프
5. **Complete**: 결과 보고

## Prerequisites

- [unity-cli](https://github.com/geuneda/unity-cli) (브릿지 개선 적용 필요)
- Unity Editor (unity-connector 패키지 설치)
- Claude Code CLI
- .NET SDK (net10.0+)

## Target Resolutions

| Device | Resolution | PPI |
|--------|-----------|-----|
| iPad 10th | 2160x1620 | 265 |
| iPhone 13 Pro | 2532x1170 | 457 |
| Galaxy S21 | 2340x1080 | 443 |
| Mid-range Android | 2280x1080 | 402 |
| Galaxy S10+ | 3040x1440 | 522 |

## Project Structure

```
ugui-from-screenshot/
  README.md                    -- This file
  PLAN.md                      -- Detailed implementation plan
  skill/
    SKILL.md                   -- Claude Code skill definition
    references/
      new-commands.md          -- New unity-cli commands reference
      anchoring-strategy.md    -- Responsive anchoring patterns
      resolution-profiles.md   -- Multi-resolution test profiles
  bridge-patches/
    CHANGES.md                 -- UnityCliBridge.cs modifications
```

## Related

- [unity-cli](https://github.com/geuneda/unity-cli) - Unity Editor CLI tool
