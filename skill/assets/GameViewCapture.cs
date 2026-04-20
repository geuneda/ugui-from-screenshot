// 프로젝트에 복사: Assets/_Project/Scripts/Editor/GameViewCapture.cs
//
// 배경: unity-cli에 ui.screenshot.capture 명령이 없다. 또한 Canvas가 Overlay 모드면
// 일반 카메라 캡처로 잡히지 않는다. CanvasScaler가 ScaleWithScreenSize면 GameView의
// Screen.width/height와 RT 크기 불일치로 축소 렌더링이 발생한다.
//
// 이 유틸리티는 캡처 직전에 Canvas를 ScreenSpaceCamera + ConstantPixelSize(scale=1.0)
// 로 임시 전환하여 픽셀 단위로 의도대로 렌더링시키고, 완료 후 원복한다.
//
// 사용:
//   unity-cli menu execute path="Tools/Capture Game View"      → 전체 Canvas 캡처
//   unity-cli menu execute path="Tools/Capture Card Only"      → 984x666 카드 중심 캡처
//
// 출력 경로: Assets/_Temp/GameCapture.png, Assets/_Temp/CardCapture.png

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpellDefense.Editor
{
    public static class GameViewCapture
    {
        [MenuItem("Tools/Capture Game View")]
        public static void CaptureFull() => Capture(1440, 3040, "Assets/_Temp/GameCapture.png");

        [MenuItem("Tools/Capture Card Only")]
        public static void CaptureCardOnly() => Capture(984, 666, "Assets/_Temp/CardCapture.png");

        static void Capture(int w, int h, string outputPath)
        {
            string dir = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            if (canvases.Length == 0)
            {
                Debug.LogWarning("[GameViewCapture] No canvas in scene");
                return;
            }

            var origModes = new RenderMode[canvases.Length];
            var origCams = new Camera[canvases.Length];
            var origScaleMode = new CanvasScaler.ScaleMode[canvases.Length];
            var origScaleFactor = new float[canvases.Length];
            var origRefRes = new Vector2[canvases.Length];
            var origMatch = new float[canvases.Length];
            var scalers = new CanvasScaler[canvases.Length];

            var camGo = new GameObject("__CaptureCam");
            camGo.transform.position = new Vector3(0, 0, -100);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.94f, 0.96f, 0.98f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = h / 2f;
            cam.aspect = (float)w / h;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;

            var rt = new RenderTexture(w, h, 24);
            rt.antiAliasing = 1;
            cam.targetTexture = rt;

            for (int i = 0; i < canvases.Length; i++)
            {
                origModes[i] = canvases[i].renderMode;
                origCams[i] = canvases[i].worldCamera;
                scalers[i] = canvases[i].GetComponent<CanvasScaler>();
                if (scalers[i] != null)
                {
                    origScaleMode[i] = scalers[i].uiScaleMode;
                    origScaleFactor[i] = scalers[i].scaleFactor;
                    origRefRes[i] = scalers[i].referenceResolution;
                    origMatch[i] = scalers[i].matchWidthOrHeight;

                    scalers[i].uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scalers[i].scaleFactor = 1f;
                }
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = cam;
                canvases[i].planeDistance = 10;
            }

            Canvas.ForceUpdateCanvases();
            cam.Render();

            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // 원복 (try/finally가 아닌 이유: 예외 발생 시 디버깅 위해 상태 보존이 유용할 수 있음)
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = origModes[i];
                canvases[i].worldCamera = origCams[i];
                if (scalers[i] != null)
                {
                    scalers[i].uiScaleMode = origScaleMode[i];
                    scalers[i].scaleFactor = origScaleFactor[i];
                    scalers[i].referenceResolution = origRefRes[i];
                    scalers[i].matchWidthOrHeight = origMatch[i];
                }
            }

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(rt);

            File.WriteAllBytes(outputPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.Refresh();
            Debug.Log($"[GameViewCapture] Saved {w}x{h} to {outputPath}");
        }
    }
}
