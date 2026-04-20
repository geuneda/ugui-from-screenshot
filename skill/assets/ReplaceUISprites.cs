// 프로젝트에 복사: Assets/_Project/Scripts/Editor/ReplaceUISprites.cs
//
// 배경: unity-cli의 ui.image.create로 만든 Image는 sprite가 null이라도 Unity의
// 기본 UISprite(둥근 모서리)로 렌더링된다. 각진 사각형을 원하면 프로젝트의
// 평면 화이트 스프라이트로 일괄 교체하는 게 가장 빠르다.
//
// 프로젝트마다 사용할 스프라이트 경로가 다르므로 TargetSpritePath를 수정한다.
//
// 사용:
//   unity-cli menu execute path="Tools/Replace UI Images With Flat Sprite"
//
// 팁: 프로젝트에 Placeholders/HpBarWhite.png 같은 1x1 화이트가 있다면 재사용.
// 없으면 아주 작은 흰색 PNG를 만들어 두고 경로를 바꾼다.

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpellDefense.Editor
{
    public static class ReplaceUISprites
    {
        // TODO: 프로젝트에 맞게 수정
        const string TargetSpritePath = "Assets/_Project/Art/Sprites/Placeholders/HpBarWhite.png";

        [MenuItem("Tools/Replace UI Images With Flat Sprite")]
        public static void ReplaceAll()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TargetSpritePath);
            if (sprite == null)
            {
                Debug.LogError($"[ReplaceUISprites] Sprite not found at {TargetSpritePath}. " +
                               "Edit TargetSpritePath in this file.");
                return;
            }

            var images = Object.FindObjectsByType<Image>(FindObjectsSortMode.None);
            int n = 0;
            foreach (var img in images)
            {
                img.sprite = sprite;
                img.type = Image.Type.Simple;
                EditorUtility.SetDirty(img);
                n++;
            }
            Debug.Log($"[ReplaceUISprites] Replaced sprite on {n} images");
        }
    }
}
