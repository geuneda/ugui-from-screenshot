// UnityToFigmaBootstrap.cs
// 위치: 프로젝트 Assets/_Project/Scripts/Editor/ 에 복사해서 사용.
//
// 역할: Figma → Unity 자동 임포트(UnityToFigma 패키지)를 CLI/스크립트로 다룰 수 있도록
//       다이얼로그 발생 가능성을 최대한 제거한 부트스트랩 + 결과 리포트 메뉴 모음.
//
// 의존: UnityToFigma 1.0.9+ 패키지가 설치되어 있어야 한다.
//      (com.simonoliver.unitytofigma, https://github.com/geuneda/UnityToFigma.git)
//      TextMesh Pro Essential Resources 도 사전 임포트 필요.
//
// 환경변수 / EditorPrefs (모두 선택):
//   FIGMA_DOCUMENT_URL                Figma 문서 URL (없으면 EditorPrefs "ugui.figma.documentUrl" 사용)
//   FIGMA_PAT                          Figma Personal Access Token (없으면 EditorPrefs "ugui.figma.pat" 사용)
//   UGUI_FIGMA_BUILD_PROTOTYPE_FLOW    "true" 면 PrototypeFlow 빌드 (기본 false 로 강제)
//   UGUI_FIGMA_REPORT_PATH             결과 JSON 출력 경로 (기본 Assets/_Temp/UnityToFigmaReport.json)
//
// 메뉴:
//   Tools/UnityToFigma Bootstrap/Prepare Settings   - 설정/PAT 자동 주입 (다이얼로그 회피 사전 작업)
//   Tools/UnityToFigma Bootstrap/Sync Document      - Prepare 후 UnityToFigma/Sync Document 호출
//   Tools/UnityToFigma Bootstrap/Dump Last Report   - 임포트 결과(생성된 Screens/Components/Textures) JSON 으로 dump
//   Tools/UnityToFigma Bootstrap/Instantiate Default Screen
//                                                    - 임포트된 첫 Screen 프리팹을 현재 씬 Canvas 아래에 인스턴스화
//
// 비고:
//   - BuildPrototypeFlow=false 로 강제하면 RunTimeAssetsScenePath / 씬 전환 다이얼로그가 발생하지 않는다.
//   - PAT 은 PlayerPrefs("FIGMA_PERSONAL_ACCESS_TOKEN") 으로 저장 (UnityToFigma 패키지 규약).
//   - 설정 에셋이 없으면 UnityToFigma 패키지의 Provider 가 만들어 주는 기본 위치
//     (Assets/UnityToFigmaSettings.asset) 에 자동 생성한다.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UguiFromScreenshot.Editor
{
    public static class UnityToFigmaBootstrap
    {
        private const string FIGMA_PAT_PREF_KEY = "FIGMA_PERSONAL_ACCESS_TOKEN";
        private const string EDITOR_PREF_URL = "ugui.figma.documentUrl";
        private const string EDITOR_PREF_PAT = "ugui.figma.pat";
        private const string DEFAULT_REPORT_PATH = "Assets/_Temp/UnityToFigmaReport.json";
        private const string SETTINGS_ASSET_PATH = "Assets/UnityToFigmaSettings.asset";
        private const string UNITY_TO_FIGMA_SYNC_MENU = "UnityToFigma/Sync Document";

        // ----------------------------------------------------------------- Prepare

        [MenuItem("Tools/UnityToFigma Bootstrap/Prepare Settings", priority = 1)]
        public static void PrepareSettings()
        {
            var url = ResolveDocumentUrl();
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError(
                    "[UnityToFigmaBootstrap] Figma document URL 이 없습니다. " +
                    "FIGMA_DOCUMENT_URL 환경변수 또는 EditorPrefs '" + EDITOR_PREF_URL + "' 를 설정하세요.");
                return;
            }

            var pat = ResolvePat();
            if (string.IsNullOrEmpty(pat))
            {
                Debug.LogError(
                    "[UnityToFigmaBootstrap] Figma PAT 이 없습니다. " +
                    "FIGMA_PAT 환경변수 또는 EditorPrefs '" + EDITOR_PREF_PAT + "' 를 설정하세요.");
                return;
            }

            // 1) PlayerPrefs 에 PAT 저장 (UnityToFigma 가 이 키에서 읽음)
            PlayerPrefs.SetString(FIGMA_PAT_PREF_KEY, pat);
            PlayerPrefs.Save();

            // 2) 설정 에셋 찾거나 생성
            var settings = LoadOrCreateSettingsAsset();
            if (settings == null)
            {
                Debug.LogError("[UnityToFigmaBootstrap] UnityToFigmaSettings 에셋 생성 실패.");
                return;
            }

            // 3) 다이얼로그 회피 / 권장 옵션 강제
            ApplySettings(settings, url);

            // 4) TMP Essentials 확인
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
            {
                Debug.LogWarning(
                    "[UnityToFigmaBootstrap] TextMesh Pro Essential Resources 가 임포트되지 않았습니다. " +
                    "메뉴: Window > TextMeshPro > Import TMP Essential Resources 실행 후 다시 시도하세요.");
            }

            Debug.Log(
                $"[UnityToFigmaBootstrap] Prepare 완료. url={url}, settings={AssetDatabase.GetAssetPath(settings)}, " +
                "BuildPrototypeFlow=" + GetBuildPrototypeFlow(settings));
        }

        // ----------------------------------------------------------------- Sync

        [MenuItem("Tools/UnityToFigma Bootstrap/Sync Document", priority = 2)]
        public static void SyncDocument()
        {
            PrepareSettings();
            // 메뉴 큐 처리는 즉시 호출이 더 안정적이라 EditorApplication.delayCall 우회
            try
            {
                if (!EditorApplication.ExecuteMenuItem(UNITY_TO_FIGMA_SYNC_MENU))
                {
                    Debug.LogError("[UnityToFigmaBootstrap] 메뉴 실행 실패: " + UNITY_TO_FIGMA_SYNC_MENU +
                                   " (UnityToFigma 패키지가 설치되어 있는지 확인)");
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[UnityToFigmaBootstrap] Sync 중 예외: " + e);
                return;
            }
        }

        // ----------------------------------------------------------------- Dump report

        [MenuItem("Tools/UnityToFigma Bootstrap/Dump Last Report", priority = 20)]
        public static void DumpLastReport()
        {
            var settings = LoadOrCreateSettingsAsset();
            var importRoot = settings != null ? GetStringField(settings, "ImportRoot") ?? "Assets/Figma" : "Assets/Figma";
            var screensFolder = settings != null ? GetStringField(settings, "ScreensFolderName") ?? "Screens" : "Screens";
            var componentsFolder = settings != null ? GetStringField(settings, "ComponentsFolderName") ?? "Components" : "Components";
            var pagesFolder = settings != null ? GetStringField(settings, "PagesFolderName") ?? "Pages" : "Pages";
            var texturesFolder = settings != null ? GetStringField(settings, "TexturesFolderName") ?? "Textures" : "Textures";
            var serverImagesFolder = settings != null ? GetStringField(settings, "ServerRenderedImagesFolderName") ?? "ServerRenderedImages" : "ServerRenderedImages";
            var fontsFolder = settings != null ? GetStringField(settings, "FontsFolderName") ?? "Fonts" : "Fonts";

            var report = new Dictionary<string, object>
            {
                ["importRoot"] = importRoot,
                ["screens"] = ListAssets($"{importRoot}/{screensFolder}", "*.prefab"),
                ["components"] = ListAssets($"{importRoot}/{componentsFolder}", "*.prefab"),
                ["pages"] = ListAssets($"{importRoot}/{pagesFolder}", "*.prefab"),
                ["textures"] = ListAssets($"{importRoot}/{texturesFolder}", "*.png", "*.jpg"),
                ["serverImages"] = ListAssets($"{importRoot}/{serverImagesFolder}", "*.png"),
                ["fonts"] = ListAssets($"{importRoot}/{fontsFolder}", "*.asset", "*.ttf", "*.otf"),
                ["roundedSkipped"] = ScanRoundedSkipped(),
            };

            var reportPath = ResolveReportPath();
            EnsureDirectory(reportPath);
            File.WriteAllText(reportPath, ToJson(report, indent: true));
            AssetDatabase.Refresh();
            Debug.Log($"[UnityToFigmaBootstrap] 리포트 저장: {reportPath} (screens={((List<string>)report["screens"]).Count})");
        }

        // ----------------------------------------------------------------- Instantiate

        [MenuItem("Tools/UnityToFigma Bootstrap/Instantiate Default Screen", priority = 21)]
        public static void InstantiateDefaultScreen()
        {
            var settings = LoadOrCreateSettingsAsset();
            if (settings == null)
            {
                Debug.LogError("[UnityToFigmaBootstrap] 설정 에셋이 없어 인스턴스화 경로를 결정할 수 없습니다.");
                return;
            }
            var importRoot = GetStringField(settings, "ImportRoot") ?? "Assets/Figma";
            var screensFolder = GetStringField(settings, "ScreensFolderName") ?? "Screens";
            var prefabs = ListAssets($"{importRoot}/{screensFolder}", "*.prefab");
            if (prefabs.Count == 0)
            {
                Debug.LogWarning($"[UnityToFigmaBootstrap] {importRoot}/{screensFolder} 에 Screen 프리팹이 없습니다.");
                return;
            }

            var firstPath = prefabs[0];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(firstPath);
            if (prefab == null)
            {
                Debug.LogError($"[UnityToFigmaBootstrap] 프리팹 로드 실패: {firstPath}");
                return;
            }

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            Transform parent = null;
            if (canvas == null)
            {
                var go = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                parent = canvas.transform;
            }
            else
            {
                parent = canvas.transform;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Selection.activeGameObject = instance;
            Debug.Log($"[UnityToFigmaBootstrap] 인스턴스화 완료: {firstPath}");
        }

        // ----------------------------------------------------------------- Helpers

        private static string ResolveDocumentUrl()
        {
            var env = Environment.GetEnvironmentVariable("FIGMA_DOCUMENT_URL");
            if (!string.IsNullOrEmpty(env)) return env;
            var pref = EditorPrefs.GetString(EDITOR_PREF_URL, string.Empty);
            return pref;
        }

        private static string ResolvePat()
        {
            var env = Environment.GetEnvironmentVariable("FIGMA_PAT");
            if (!string.IsNullOrEmpty(env)) return env;
            var pref = EditorPrefs.GetString(EDITOR_PREF_PAT, string.Empty);
            if (!string.IsNullOrEmpty(pref)) return pref;
            // 마지막 폴백: 이미 PlayerPrefs 에 저장돼 있으면 그대로 둔다.
            var existing = PlayerPrefs.GetString(FIGMA_PAT_PREF_KEY, string.Empty);
            return existing;
        }

        private static string ResolveReportPath()
        {
            var env = Environment.GetEnvironmentVariable("UGUI_FIGMA_REPORT_PATH");
            return string.IsNullOrEmpty(env) ? DEFAULT_REPORT_PATH : env;
        }

        private static UnityEngine.Object LoadOrCreateSettingsAsset()
        {
            var settingsType = FindUnityToFigmaSettingsType();
            if (settingsType == null)
            {
                Debug.LogError("[UnityToFigmaBootstrap] UnityToFigma 패키지(com.simonoliver.unitytofigma) 가 설치되어 있지 않습니다.");
                return null;
            }

            var guids = AssetDatabase.FindAssets("t:" + settingsType.Name);
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath(path, settingsType);
            }

            var asset = ScriptableObject.CreateInstance(settingsType);
            EnsureDirectory(SETTINGS_ASSET_PATH);
            AssetDatabase.CreateAsset(asset, SETTINGS_ASSET_PATH);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static Type FindUnityToFigmaSettingsType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType("UnityToFigma.Editor.Settings.UnityToFigmaSettings"); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
        }

        private static void ApplySettings(UnityEngine.Object settings, string url)
        {
            SetField(settings, "DocumentUrl", url);

            // BuildPrototypeFlow 강제 false (다이얼로그 회피). env 로 명시적으로 켤 수 있음.
            var enableProto = string.Equals(
                Environment.GetEnvironmentVariable("UGUI_FIGMA_BUILD_PROTOTYPE_FLOW"),
                "true", StringComparison.OrdinalIgnoreCase);
            SetField(settings, "BuildPrototypeFlow", enableProto);

            // 페이지 선택 모드는 자동화에서는 일단 끔.
            SetField(settings, "OnlyImportSelectedPages", false);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private static bool GetBuildPrototypeFlow(UnityEngine.Object settings)
        {
            var f = settings.GetType().GetField("BuildPrototypeFlow", BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return false;
            var v = f.GetValue(settings);
            return v is bool b && b;
        }

        private static void SetField(UnityEngine.Object target, string name, object value)
        {
            var t = target.GetType();
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                f.SetValue(target, value);
                return;
            }
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                p.SetValue(target, value);
            }
        }

        private static string GetStringField(UnityEngine.Object target, string name)
        {
            if (target == null) return null;
            var t = target.GetType();
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return f.GetValue(target) as string;
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) return p.GetValue(target) as string;
            return null;
        }

        private static List<string> ListAssets(string folder, params string[] patterns)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(folder)) return results;
            var absolute = Path.Combine(Directory.GetCurrentDirectory(), folder);
            if (!Directory.Exists(absolute)) return results;
            foreach (var pattern in patterns)
            {
                foreach (var p in Directory.GetFiles(absolute, pattern, SearchOption.AllDirectories))
                {
                    var rel = "Assets" + p.Substring(Path.Combine(Directory.GetCurrentDirectory(), "Assets").Length).Replace('\\', '/');
                    results.Add(rel);
                }
            }
            results.Sort(StringComparer.Ordinal);
            return results;
        }

        // 라운드 처리 항목 스캔: 본 스킬은 라운드 코너 자동 보정/생성을 수행하지 않으므로,
        // UnityToFigma 가 SDF 등으로 처리한 결과 외에 추가 보정이 필요한 항목은 사용자에게 보고만 한다.
        // 여기서는 임포트 트리에서 'cornerRadius' 가 0 이 아닌 항목을 단순 카운트한다.
        private static List<object> ScanRoundedSkipped()
        {
            var results = new List<object>();
            // 단순 마커: ScreenRender 의 Image 컴포넌트들 중 sprite 가 null 이고 이름에 "round/rounded/circle" 등이
            // 들어간 노드를 보고 항목으로 본다. 정확한 cornerRadius 검사는 UnityToFigma 의 SDF 컴포넌트 의존이라
            // 본 스킬에서는 휴리스틱으로 처리한다.
            var markers = new[] { "round", "rounded", "pill", "circle" };
            var importRoot = "Assets/Figma";
            var screensFolder = "Screens";
            var folder = $"{importRoot}/{screensFolder}";
            var prefabs = ListAssets(folder, "*.prefab");
            foreach (var prefabPath in prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                {
                    var lower = t.name.ToLowerInvariant();
                    if (markers.Any(m => lower.Contains(m)))
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            ["prefab"] = prefabPath,
                            ["path"] = GetTransformPath(t),
                            ["reason"] = "name-marker",
                        });
                    }
                }
            }
            return results;
        }

        private static string GetTransformPath(Transform t)
        {
            var stack = new Stack<string>();
            while (t != null) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }

        private static void EnsureDirectory(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        // 외부 의존을 줄이기 위해 작은 JSON 직렬화기 (List/Dictionary/string/bool/int/double 만 지원).
        private static string ToJson(object value, bool indent)
        {
            var sb = new System.Text.StringBuilder();
            ToJsonInternal(value, sb, indent, 0);
            return sb.ToString();
        }

        private static void ToJsonInternal(object v, System.Text.StringBuilder sb, bool indent, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            switch (v)
            {
                case string s: sb.Append('"').Append(EscapeJson(s)).Append('"'); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i); return;
                case long l: sb.Append(l); return;
                case float f: sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> dict: WriteDict(dict, sb, indent, depth); return;
                case System.Collections.IList list: WriteList(list, sb, indent, depth); return;
                default: sb.Append('"').Append(EscapeJson(v.ToString())).Append('"'); return;
            }
        }

        private static void WriteDict(IDictionary<string, object> dict, System.Text.StringBuilder sb, bool indent, int depth)
        {
            sb.Append('{');
            var first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                if (indent) { sb.Append('\n'); sb.Append(new string(' ', (depth + 1) * 2)); }
                sb.Append('"').Append(EscapeJson(kv.Key)).Append("\":");
                if (indent) sb.Append(' ');
                ToJsonInternal(kv.Value, sb, indent, depth + 1);
            }
            if (indent && !first) { sb.Append('\n'); sb.Append(new string(' ', depth * 2)); }
            sb.Append('}');
        }

        private static void WriteList(System.Collections.IList list, System.Text.StringBuilder sb, bool indent, int depth)
        {
            sb.Append('[');
            var first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                if (indent) { sb.Append('\n'); sb.Append(new string(' ', (depth + 1) * 2)); }
                ToJsonInternal(item, sb, indent, depth + 1);
            }
            if (indent && !first) { sb.Append('\n'); sb.Append(new string(' ', depth * 2)); }
            sb.Append(']');
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
#endif
