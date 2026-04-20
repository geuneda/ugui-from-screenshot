// 프로젝트에 복사: Assets/_Project/Scripts/Editor/FixTMPSettings.cs
//
// 배경: unity-cli의 component.update로 TMP 속성을 바꿔도 mesh가 곧바로 리빌드되지
// 않는 경우가 있고, 또 일부 property(overflowMode/textWrappingMode)는 JSON으로
// 설정해도 내부 상태 전환이 불완전하다. Editor에서 직접 API로 세팅한 뒤
// ForceMeshUpdate를 호출하는 편이 가장 확실하다.
//
// 사용:
//   unity-cli menu execute path="Tools/Fix TMP Overflow Settings"
//
// 동작:
//   - 현재 씬의 모든 TextMeshProUGUI를 찾는다
//   - textWrappingMode = Normal (줄바꿈 허용)
//   - overflowMode = Overflow (잘림/ellipsis 비활성)
//   - enableAutoSizing = false
//   - ForceMeshUpdate(true, true)

using TMPro;
using UnityEditor;
using UnityEngine;

namespace SpellDefense.Editor
{
    public static class FixTMPSettings
    {
        [MenuItem("Tools/Fix TMP Overflow Settings")]
        public static void FixAll()
        {
            var texts = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
            int n = 0;
            foreach (var t in texts)
            {
                t.textWrappingMode = TextWrappingModes.Normal;
                t.overflowMode = TextOverflowModes.Overflow;
                t.enableAutoSizing = false;
                t.ForceMeshUpdate(true, true);
                EditorUtility.SetDirty(t);
                n++;
            }
            Debug.Log($"[FixTMPSettings] Fixed {n} TMP texts");
        }
    }
}
