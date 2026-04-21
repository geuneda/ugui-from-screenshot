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
// 입력 전달 (우선순위: ContextFile > 환경변수 > EditorPrefs > PlayerPrefs(PAT only)):
//
//   ContextFile (권장): {PROJECT}/Library/UguiFigmaContext.json
//     {
//       "documentUrl": "https://www.figma.com/design/.../...",
//       "pat": "figd_xxx",
//       "buildPrototypeFlow": false,
//       "reportPath": "Assets/_Temp/UnityToFigmaReport.json",
//       "syncTimeoutSeconds": 600
//     }
//     - unity-cli 는 셸 env 를 Editor 프로세스로 전달하지 않으므로 자동화에서는 ContextFile 만 신뢰 가능.
//     - Library/ 는 Unity 가 자동으로 무시하는 폴더라 git 에 들어가지 않는다.
//     - 부트스트랩이 PAT 을 PlayerPrefs 에 적용한 뒤 파일을 자동 삭제한다 (선택: keepContext=true).
//
//   환경변수 / EditorPrefs (보조, Editor 내부 GUI 사용 시):
//     FIGMA_DOCUMENT_URL                EditorPrefs "ugui.figma.documentUrl"
//     FIGMA_PAT                         EditorPrefs "ugui.figma.pat"
//     UGUI_FIGMA_BUILD_PROTOTYPE_FLOW    "true" 면 PrototypeFlow 빌드 (기본 false 로 강제)
//     UGUI_FIGMA_REPORT_PATH             결과 JSON 출력 경로 (기본 Assets/_Temp/UnityToFigmaReport.json)
//     UGUI_FIGMA_SYNC_TIMEOUT_S          Sync 완료 대기 타임아웃 (기본 600 초)
//
// 메뉴:
//   Tools/UnityToFigma Bootstrap/Prepare Settings   - 설정/PAT 자동 주입 (다이얼로그 회피 사전 작업)
//   Tools/UnityToFigma Bootstrap/Sync Document      - Prepare → Reflection 으로 UnityToFigmaImporter.Sync 호출 →
//                                                     Application.logMessageReceived 후킹으로 "UnityToFigma import:" 요약 라인 대기
//   Tools/UnityToFigma Bootstrap/Dump Last Report   - 임포트 결과(생성된 Screens/Components/Textures) JSON 으로 dump
//   Tools/UnityToFigma Bootstrap/Instantiate Default Screen
//                                                    - 임포트된 첫 Screen 프리팹을 현재 씬 Canvas 아래에 인스턴스화
//
// 비고:
//   - BuildPrototypeFlow=false 로 강제하면 RunTimeAssetsScenePath / 씬 전환 다이얼로그가 발생하지 않는다.
//   - PAT 은 PlayerPrefs("FIGMA_PERSONAL_ACCESS_TOKEN") 으로 저장 (UnityToFigma 패키지 규약).
//   - 설정 에셋이 없으면 UnityToFigma 패키지의 Provider 가 만들어 주는 기본 위치
//     (Assets/UnityToFigmaSettings.asset) 에 자동 생성한다.
//   - TMP Essentials 가 없으면 TMPro.TMP_PackageResourceImporter.ImportResources 로 자동 임포트 시도 (다이얼로그 회피).
//   - 동기화 완료 시 ${UGUI_FIGMA_REPORT_PATH} 에 자동으로 Dump Last Report 결과를 기록한다.

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
        private const string EDITOR_PREF_DEFAULT_SCREEN = "ugui.figma.defaultScreenName";
        private const string DEFAULT_REPORT_PATH = "Assets/_Temp/UnityToFigmaReport.json";
        private const string SETTINGS_ASSET_PATH = "Assets/UnityToFigmaSettings.asset";
        private const string UNITY_TO_FIGMA_SYNC_MENU = "UnityToFigma/Sync Document";
        private const string CONTEXT_FILE_RELATIVE = "Library/UguiFigmaContext.json";
        private const double DEFAULT_SYNC_TIMEOUT_S = 600d;

        private static Dictionary<string, object> s_Context;

        private static double s_SyncStartedAt;
        private static bool s_SyncRunning;
        private static bool s_SyncSucceeded;
        private static bool s_TmpImportRequested;
        private static string s_SyncSummaryLine;
        private static string s_SyncFailureMessage;

        // ----------------------------------------------------------------- Prepare

        [MenuItem("Tools/UnityToFigma Bootstrap/Prepare Settings", priority = 1)]
        public static void PrepareSettings()
        {
            LoadContextFileIfPresent();
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

        // ----------------------------------------------------------------- Page Selection
        //
        // ContextFile.selectedPages (string[]) 가 있으면 OnlyImportSelectedPages=true 로 켜고
        // settings.PageDataList 에서 매칭되는 페이지만 Selected=true 로 설정한다.
        // 매칭 규칙: 정확 이름 (case-insensitive) 또는 끝에 '*' 와일드카드 prefix.
        //
        // PageDataList 는 UnityToFigma 가 처음 sync 시 RefreshForUpdatedPages 로 채우는데,
        // 비어있으면 다이얼로그로 ReportError 가 뜬다 (다이얼로그 회피 정책 위반).
        // 이 메뉴는 UnityToFigmaSettingsPageSelectionHelper.RefreshPageListAsync 를 reflection 으로 호출해
        // PageDataList 를 비동기로 채운 뒤 EditorApplication.update 폴링으로 완료를 감지하고
        // selectedPages 매칭만 Selected=true 로 정리한다.
        //
        // selectedPages 가 비어있으면 OnlyImportSelectedPages=false 로 명시적 해제 후 즉시 종료.
        [MenuItem("Tools/UnityToFigma Bootstrap/Prepare Page Selection", priority = 3)]
        public static void PreparePageSelection()
        {
            LoadContextFileIfPresent();
            var settings = LoadOrCreateSettingsAsset();
            if (settings == null)
            {
                Debug.LogError("[UnityToFigmaBootstrap] settings 에셋을 찾지 못해 PageSelection 준비 실패.");
                return;
            }

            // PAT 적용 (RefreshPageListAsync 가 다운로드를 위해 필요)
            ApplyPatFromAllSources();

            var requested = ContextStringList("selectedPages");
            if (requested == null || requested.Count == 0)
            {
                SetField(settings, "OnlyImportSelectedPages", false);
                ClearPageDataList(settings);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                Debug.Log("[UnityToFigmaBootstrap] selectedPages 미지정 → 모든 페이지 임포트 모드로 정상화.");
                s_PageSelectionPending = false;
                return;
            }

            // OnlyImportSelectedPages 켜고 페이지 목록 다운로드 트리거
            SetField(settings, "OnlyImportSelectedPages", true);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            var helperType = FindType("UnityToFigma.Editor.Settings.UnityToFigmaSettingsPageSelectionHelper");
            if (helperType == null)
            {
                Debug.LogError("[UnityToFigmaBootstrap] PageSelectionHelper 타입 미발견.");
                return;
            }
            var refreshMethod = helperType.GetMethod("RefreshPageListAsync", BindingFlags.Static | BindingFlags.Public);
            if (refreshMethod == null)
            {
                Debug.LogError("[UnityToFigmaBootstrap] RefreshPageListAsync 미발견.");
                return;
            }

            // 기존 PageDataList 비우고 재요청 → 다운로드 후 RefreshForUpdatedPages 가 다시 채움
            ClearPageDataList(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            s_PageSelectionRequested = requested;
            s_PageSelectionStartedAt = EditorApplication.timeSinceStartup;
            s_PageSelectionPending = true;
            EditorApplication.update += OnPageSelectionTick;

            try
            {
                refreshMethod.Invoke(null, new object[] { settings });
                Debug.Log($"[UnityToFigmaBootstrap] PageSelection 다운로드 시작 (요청 {requested.Count} 개): " +
                          string.Join(", ", requested));
            }
            catch (Exception e)
            {
                EditorApplication.update -= OnPageSelectionTick;
                s_PageSelectionPending = false;
                Debug.LogError("[UnityToFigmaBootstrap] PageSelection 다운로드 호출 실패: " + e);
            }
        }

        private static void OnPageSelectionTick()
        {
            if (!s_PageSelectionPending)
            {
                EditorApplication.update -= OnPageSelectionTick;
                return;
            }
            var elapsed = EditorApplication.timeSinceStartup - s_PageSelectionStartedAt;
            if (elapsed > 60d)
            {
                EditorApplication.update -= OnPageSelectionTick;
                s_PageSelectionPending = false;
                Debug.LogError("[UnityToFigmaBootstrap] PageSelection 다운로드 60s 안에 완료되지 않음. 네트워크/PAT 확인.");
                return;
            }

            var settings = LoadOrCreateSettingsAsset();
            if (settings == null) return;
            var pageList = GetPageDataList(settings);
            if (pageList == null || pageList.Count == 0) return;

            // PageDataList 가 채워졌다 → selectedPages 매칭 적용
            EditorApplication.update -= OnPageSelectionTick;
            s_PageSelectionPending = false;

            int selectedCount = 0;
            int totalCount = pageList.Count;
            var matchedNames = new List<string>();
            var unmatchedRequested = new List<string>(s_PageSelectionRequested);

            foreach (var entry in pageList)
            {
                var nameField = entry.GetType().GetField("Name");
                var selField = entry.GetType().GetField("Selected");
                if (nameField == null || selField == null) continue;

                var pageName = nameField.GetValue(entry) as string ?? "";
                bool match = false;
                foreach (var pattern in s_PageSelectionRequested)
                {
                    if (MatchPageName(pageName, pattern))
                    {
                        match = true;
                        unmatchedRequested.Remove(pattern);
                        break;
                    }
                }
                selField.SetValue(entry, match);
                if (match)
                {
                    selectedCount++;
                    matchedNames.Add(pageName);
                }
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log($"[UnityToFigmaBootstrap] PageSelection 적용 완료: " +
                      $"selected={selectedCount}/{totalCount}, matched=[{string.Join(", ", matchedNames)}]" +
                      (unmatchedRequested.Count > 0 ? $", unmatched=[{string.Join(", ", unmatchedRequested)}]" : ""));

            if (selectedCount == 0)
            {
                Debug.LogError("[UnityToFigmaBootstrap] PageSelection 매칭된 페이지가 없습니다. " +
                               "selectedPages 패턴을 점검하세요. (정확 이름 또는 'Prefix*' 와일드카드)");
            }
        }

        private static bool MatchPageName(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (pattern.EndsWith("*", StringComparison.Ordinal))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static System.Collections.IList GetPageDataList(UnityEngine.Object settings)
        {
            var f = settings.GetType().GetField("PageDataList", BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return null;
            return f.GetValue(settings) as System.Collections.IList;
        }

        private static void ClearPageDataList(UnityEngine.Object settings)
        {
            var list = GetPageDataList(settings);
            list?.Clear();
        }

        private static void ApplyPatFromAllSources()
        {
            var pat = ResolvePat();
            if (!string.IsNullOrEmpty(pat) && PlayerPrefs.GetString(FIGMA_PAT_PREF_KEY) != pat)
            {
                PlayerPrefs.SetString(FIGMA_PAT_PREF_KEY, pat);
                PlayerPrefs.Save();
            }
        }

        private static List<string> s_PageSelectionRequested;
        private static double s_PageSelectionStartedAt;
        private static bool s_PageSelectionPending;

        // ----------------------------------------------------------------- Sync

        [MenuItem("Tools/UnityToFigma Bootstrap/Sync Document", priority = 2)]
        public static void SyncDocument()
        {
            if (s_SyncRunning)
            {
                Debug.LogWarning("[UnityToFigmaBootstrap] 이전 Sync 가 아직 실행 중입니다. 무시.");
                return;
            }

            LoadContextFileIfPresent();
            PrepareSettings();

            // PAT 재확인 (PrepareSettings 내부에서 로깅하지만 중복 가드)
            var pat = PlayerPrefs.GetString(FIGMA_PAT_PREF_KEY);
            if (string.IsNullOrEmpty(pat))
            {
                Debug.LogError("[UnityToFigmaBootstrap] PAT 이 PlayerPrefs 에 없어 Sync 를 시작할 수 없습니다.");
                return;
            }

            // TMP Essentials 자동 임포트 시도 (다이얼로그 회피)
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
            {
                if (TryAutoImportTmpEssentials())
                {
                    s_TmpImportRequested = true;
                    Debug.Log("[UnityToFigmaBootstrap] TMP Essential Resources 자동 임포트 호출. 컴파일 후 다시 Sync 메뉴를 실행하세요.");
                    return;
                }
                Debug.LogError("[UnityToFigmaBootstrap] TMP Essentials 자동 임포트 실패. " +
                               "수동: Window > TextMeshPro > Import TMP Essential Resources.");
                return;
            }

            s_SyncRunning = true;
            s_SyncSucceeded = false;
            s_SyncSummaryLine = null;
            s_SyncFailureMessage = null;
            s_SyncStartedAt = EditorApplication.timeSinceStartup;

            Application.logMessageReceived += OnSyncLogMessage;
            EditorApplication.update += OnSyncTick;

            // Reflection 으로 UnityToFigmaImporter.Sync 직접 호출 (ExecuteMenuItem 보다 안정적)
            try
            {
                var importerType = FindType("UnityToFigma.Editor.UnityToFigmaImporter");
                if (importerType == null)
                {
                    FailSync("UnityToFigmaImporter 타입을 찾을 수 없습니다 (패키지 미설치?).");
                    return;
                }
                var syncMethod = importerType.GetMethod("Sync",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (syncMethod == null)
                {
                    // 폴백: 메뉴 호출
                    if (!EditorApplication.ExecuteMenuItem(UNITY_TO_FIGMA_SYNC_MENU))
                    {
                        FailSync("UnityToFigmaImporter.Sync 메서드도, '" + UNITY_TO_FIGMA_SYNC_MENU + "' 메뉴 호출도 실패.");
                        return;
                    }
                    Debug.Log("[UnityToFigmaBootstrap] Reflection 실패 → 메뉴 호출로 폴백.");
                }
                else
                {
                    Debug.Log("[UnityToFigmaBootstrap] UnityToFigmaImporter.Sync 호출.");
                    syncMethod.Invoke(null, null);
                }
            }
            catch (Exception e)
            {
                FailSync("Sync 호출 중 예외: " + e);
            }
        }

        private static bool TryAutoImportTmpEssentials()
        {
            try
            {
                var t = FindType("TMPro.TMP_PackageResourceImporter");
                if (t == null) return false;
                var m = t.GetMethod("ImportResources", BindingFlags.Public | BindingFlags.Static);
                if (m == null) return false;
                var ps = m.GetParameters();
                object[] args;
                if (ps.Length == 3) args = new object[] { true, false, false };
                else if (ps.Length == 0) args = Array.Empty<object>();
                else return false;
                m.Invoke(null, args);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UnityToFigmaBootstrap] TMP 자동 임포트 예외: " + e.Message);
                return false;
            }
        }

        private static void OnSyncLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!s_SyncRunning) return;
            if (string.IsNullOrEmpty(condition)) return;

            if (condition.StartsWith("UnityToFigma import:", StringComparison.Ordinal))
            {
                s_SyncSummaryLine = condition;
                s_SyncSucceeded = true;
                s_SyncRunning = false;
                return;
            }

            if (type == LogType.Error || type == LogType.Exception)
            {
                if (condition.IndexOf("UnityToFigma", StringComparison.Ordinal) >= 0 ||
                    condition.IndexOf("Figma", StringComparison.Ordinal) >= 0)
                {
                    FailSync("에러 로그: " + condition);
                }
                return;
            }

            if (type == LogType.Warning)
            {
                if (condition.IndexOf("Error downloading Figma", StringComparison.Ordinal) >= 0 ||
                    condition.IndexOf("Error generating Figma document", StringComparison.Ordinal) >= 0)
                {
                    FailSync("실패성 경고 로그: " + condition);
                }
            }
        }

        private static void OnSyncTick()
        {
            if (!s_SyncRunning && s_SyncSummaryLine == null && s_SyncFailureMessage == null)
                return;

            // 타임아웃 체크
            if (s_SyncRunning)
            {
                var timeout = ResolveSyncTimeoutSecondsFromContext();
                if (EditorApplication.timeSinceStartup - s_SyncStartedAt > timeout)
                {
                    FailSync($"Sync 타임아웃 ({timeout:0}s). 큰 문서면 UGUI_FIGMA_SYNC_TIMEOUT_S 를 늘리세요.");
                }
                return;
            }

            // 종료 처리 (성공/실패 어느 쪽이든 한 번만)
            Application.logMessageReceived -= OnSyncLogMessage;
            EditorApplication.update -= OnSyncTick;

            if (s_SyncSucceeded)
            {
                Debug.Log("[UnityToFigmaBootstrap] Sync 완료: " + s_SyncSummaryLine);
                try { DumpLastReport(); }
                catch (Exception e) { Debug.LogWarning("[UnityToFigmaBootstrap] Dump 실패: " + e.Message); }
            }
            else
            {
                Debug.LogError("[UnityToFigmaBootstrap] Sync 실패: " + s_SyncFailureMessage);
            }

            DeleteContextFileUnlessKept();

            s_SyncSummaryLine = null;
            s_SyncFailureMessage = null;
            s_SyncSucceeded = false;
        }

        private static void FailSync(string message)
        {
            s_SyncFailureMessage = message;
            s_SyncSucceeded = false;
            s_SyncRunning = false;
        }

        private static double ResolveSyncTimeoutSeconds()
        {
            var raw = Environment.GetEnvironmentVariable("UGUI_FIGMA_SYNC_TIMEOUT_S");
            if (double.TryParse(raw, out var v) && v > 0) return v;
            return DEFAULT_SYNC_TIMEOUT_S;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
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

            var screenPaths = ListAssets($"{importRoot}/{screensFolder}", "*.prefab");
            var roundedScan = ScanRounded(screenPaths);

            var report = new Dictionary<string, object>
            {
                ["importRoot"] = importRoot,
                ["screens"] = screenPaths,
                ["components"] = ListAssets($"{importRoot}/{componentsFolder}", "*.prefab"),
                ["pages"] = ListAssets($"{importRoot}/{pagesFolder}", "*.prefab"),
                ["textures"] = ListAssets($"{importRoot}/{texturesFolder}", "*.png", "*.jpg"),
                ["serverImages"] = ListAssets($"{importRoot}/{serverImagesFolder}", "*.png"),
                ["fonts"] = ListAssets($"{importRoot}/{fontsFolder}", "*.asset", "*.ttf", "*.otf"),
                ["roundedHandled"] = roundedScan["handled"],
                ["roundedExtreme"] = roundedScan["extreme"],
                ["roundedSkipped"] = roundedScan["skipped"],
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
            LoadContextFileIfPresent();

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

            // ContextFile/EditorPrefs 의 defaultScreenName 우선
            string targetPath = null;
            var requestedName = ContextString("defaultScreenName")
                                ?? Environment.GetEnvironmentVariable("UGUI_FIGMA_DEFAULT_SCREEN")
                                ?? EditorPrefs.GetString(EDITOR_PREF_DEFAULT_SCREEN, null);
            if (!string.IsNullOrEmpty(requestedName))
            {
                foreach (var p in prefabs)
                {
                    var n = Path.GetFileNameWithoutExtension(p);
                    if (string.Equals(n, requestedName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetPath = p;
                        break;
                    }
                }
                // UnityToFigma 는 다른 페이지에 동일 이름의 Frame 이 있을 때 _1, _2 suffix 를 붙인다.
                // 사용자가 "MainScreen" 을 요청했는데 그 이름이 다른 페이지에서 선점되어 우리가
                // 만든 화면이 "MainScreen_1" 으로 저장된 케이스를 자동 매칭한다.
                if (targetPath == null)
                {
                    string bestSuffixMatch = null;
                    int bestSuffixIndex = int.MaxValue;
                    foreach (var p in prefabs)
                    {
                        var n = Path.GetFileNameWithoutExtension(p);
                        if (n.StartsWith(requestedName + "_", StringComparison.OrdinalIgnoreCase))
                        {
                            var tail = n.Substring(requestedName.Length + 1);
                            if (int.TryParse(tail, out var idx) && idx >= 0 && idx < bestSuffixIndex)
                            {
                                bestSuffixIndex = idx;
                                bestSuffixMatch = p;
                            }
                        }
                    }
                    if (bestSuffixMatch != null)
                    {
                        targetPath = bestSuffixMatch;
                        Debug.Log($"[UnityToFigmaBootstrap] '{requestedName}' 를 못 찾아 suffix 후보로 폴백: " +
                                  $"{Path.GetFileNameWithoutExtension(bestSuffixMatch)} (UnityToFigma 가 페이지 간 이름 충돌로 _{bestSuffixIndex} 부여)");
                    }
                }
                if (targetPath == null)
                {
                    Debug.LogWarning($"[UnityToFigmaBootstrap] 지정된 defaultScreenName='{requestedName}' 를 못 찾아 첫 프리팹으로 폴백합니다.");
                }
            }
            if (targetPath == null) targetPath = prefabs[0];

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (prefab == null)
            {
                Debug.LogError($"[UnityToFigmaBootstrap] 프리팹 로드 실패: {targetPath}");
                return;
            }

            // 같은 이름이 이미 씬에 있으면 제거 (반복 호출 시 중복 방지)
            var existing = GameObject.Find(prefab.name);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing);
                Debug.Log($"[UnityToFigmaBootstrap] 기존 인스턴스 제거: {prefab.name}");
            }

            // ContextFile/EditorPrefs 의 cleanOtherScreens(기본 true) 가 true 면
            // 같은 Canvas 안의 다른 Screen prefab 인스턴스도 모두 제거한다.
            // (하나의 Canvas 에 여러 화면이 겹쳐 보이는 문제 방지)
            var cleanOthers = ContextBool("cleanOtherScreens") ?? true;
            if (cleanOthers)
            {
                CleanOtherScreensInCanvas(prefabs, prefab.name);
            }

            // 더 강력한 옵션: clearCanvasOnInstantiate(기본 true) 가 true 면
            // 검색 대상 Canvas 의 모든 자식을 제거한 뒤 새 Screen 만 남긴다.
            // unpack 된 prefab 잔재 등 이름으로 추적 불가능한 객체까지 깨끗이 청소한다.
            var clearCanvas = ContextBool("clearCanvasOnInstantiate") ?? true;
            if (clearCanvas)
            {
                ClearTargetCanvas(prefab.name);
            }

            // 핵심 워크플로우: Screen prefab 의 사이즈(RectTransform.sizeDelta) 를 읽어
            // Canvas referenceResolution 으로 사용한다. UnityToFigma 가 만든 Screen 은
            // Figma 최상위 Frame 사이즈를 그대로 보존하므로(예: 1440x3040), 이를 기준 해상도로
            // 잡으면 다해상도 환경에서 비율이 깨지지 않는다.
            var prefabRT = prefab.GetComponent<RectTransform>();
            Vector2 referenceResolution = new Vector2(1080f, 1920f);
            if (prefabRT != null && prefabRT.rect.width > 1f && prefabRT.rect.height > 1f)
            {
                referenceResolution = new Vector2(prefabRT.rect.width, prefabRT.rect.height);
            }

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            Transform parent;
            if (canvas == null)
            {
                var go = new GameObject("UICanvas",
                    typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler),
                    typeof(UnityEngine.UI.GraphicRaycaster));
                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                parent = canvas.transform;
            }
            else
            {
                parent = canvas.transform;
            }

            // CanvasScaler 를 Screen prefab 사이즈에 맞춤
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // Screen 을 Canvas 안에 풀스트레치로 정합 (Figma 좌표 그대로 보이게)
            var rt = instance.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            Selection.activeGameObject = instance;
            EditorSceneRefresh();
            Debug.Log($"[UnityToFigmaBootstrap] 인스턴스화 완료: {targetPath} " +
                      $"(parent={parent.name}, referenceResolution={referenceResolution.x}x{referenceResolution.y})");

            // GameView 종횡비도 자동으로 디자인에 맞춘다 (cleanOtherScreens 와 동일하게 기본 활성)
            var syncAspect = ContextBool("syncGameViewAspect") ?? true;
            if (syncAspect && prefabRT != null)
            {
                int width = Mathf.RoundToInt(prefabRT.rect.width);
                int height = Mathf.RoundToInt(prefabRT.rect.height);
                ApplyGameViewAspect(width, height, prefab.name);
            }
        }

        private static void ClearTargetCanvas(string keepName)
        {
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) return;
            int removed = 0;
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i).gameObject;
                if (child.name == keepName) continue;
                UnityEngine.Object.DestroyImmediate(child);
                removed++;
            }
            if (removed > 0)
            {
                Debug.Log($"[UnityToFigmaBootstrap] Canvas 청소: {removed} 개 자식 제거 (남긴 이름='{keepName}')");
            }
        }

        private static void CleanOtherScreensInCanvas(List<string> screenPrefabPaths, string keepName)
        {
            var screenNames = new HashSet<string>();
            foreach (var p in screenPrefabPaths)
            {
                screenNames.Add(Path.GetFileNameWithoutExtension(p));
            }
            int removed = 0;
            // 씬 전체에서 Screen prefab 이름과 일치하는 GameObject 를 찾아 제거.
            // (씬 루트에 떠있는 잔재 + Canvas 자식 모두 포함)
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            var rootGOs = scene.GetRootGameObjects();
            foreach (var root in rootGOs)
            {
                if (root == null) continue;
                if (root.name != keepName && screenNames.Contains(root.name))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    removed++;
                    continue;
                }
                // 자식들도 검사 (다른 Canvas 안에 있을 수도 있음)
                var allChildren = root.GetComponentsInChildren<Transform>(true);
                foreach (var t in allChildren)
                {
                    if (t == null || t.gameObject == null) continue;
                    if (t.gameObject == root) continue;
                    if (t.gameObject.name != keepName && screenNames.Contains(t.gameObject.name))
                    {
                        UnityEngine.Object.DestroyImmediate(t.gameObject);
                        removed++;
                    }
                }
            }
            if (removed > 0)
            {
                Debug.Log($"[UnityToFigmaBootstrap] 씬 내 다른 Screen 인스턴스 정리: {removed} 개");
            }
        }

        // GameView Aspect 를 Screen prefab 의 종횡비에 맞춰 자동 변경한다.
        // referenceResolution 만 맞추면 ScaleWithScreenSize=Expand 가 화면을 축소만 시키므로,
        // 디자인 전체가 GameView 에 보이려면 GameView 자체의 종횡비를 디자인과 맞춰야 한다.
        [MenuItem("Tools/UnityToFigma Bootstrap/Sync GameView Aspect To Default Screen", priority = 22)]
        public static void SyncGameViewAspectToDefaultScreen()
        {
            LoadContextFileIfPresent();

            var settings = LoadOrCreateSettingsAsset();
            if (settings == null) { Debug.LogError("[UnityToFigmaBootstrap] settings 없음"); return; }
            var importRoot = GetStringField(settings, "ImportRoot") ?? "Assets/Figma";
            var screensFolder = GetStringField(settings, "ScreensFolderName") ?? "Screens";
            var prefabs = ListAssets($"{importRoot}/{screensFolder}", "*.prefab");
            if (prefabs.Count == 0) { Debug.LogWarning("[UnityToFigmaBootstrap] Screen prefab 없음"); return; }

            var requestedName = ContextString("defaultScreenName")
                                ?? Environment.GetEnvironmentVariable("UGUI_FIGMA_DEFAULT_SCREEN")
                                ?? EditorPrefs.GetString(EDITOR_PREF_DEFAULT_SCREEN, null);
            var targetPath = ResolveTargetScreenPath(prefabs, requestedName);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            var rt = prefab != null ? prefab.GetComponent<RectTransform>() : null;
            if (rt == null) { Debug.LogError("[UnityToFigmaBootstrap] prefab RectTransform 없음"); return; }

            int width = Mathf.RoundToInt(rt.rect.width);
            int height = Mathf.RoundToInt(rt.rect.height);
            ApplyGameViewAspect(width, height, prefab.name);
        }

        // 디자인 사이즈 (Screen prefab.referenceResolution) 그대로 RT 캡처.
        // unity-cli ui screenshot.capture 가 GameView 의 현재 종횡비 그대로 찍어서 검증이 어려운 문제를 보완한다.
        // CanvasScaler 를 일시적으로 ConstantPixelSize 로 바꿔 픽셀 단위 정확 렌더링 후 원복.
        [MenuItem("Tools/UnityToFigma Bootstrap/Capture Default Screen", priority = 23)]
        public static void CaptureDefaultScreen()
        {
            LoadContextFileIfPresent();

            var settings = LoadOrCreateSettingsAsset();
            if (settings == null) { Debug.LogError("[UnityToFigmaBootstrap] settings 없음"); return; }
            var importRoot = GetStringField(settings, "ImportRoot") ?? "Assets/Figma";
            var screensFolder = GetStringField(settings, "ScreensFolderName") ?? "Screens";
            var prefabs = ListAssets($"{importRoot}/{screensFolder}", "*.prefab");
            if (prefabs.Count == 0) { Debug.LogWarning("[UnityToFigmaBootstrap] Screen prefab 없음"); return; }

            var requestedName = ContextString("defaultScreenName")
                                ?? Environment.GetEnvironmentVariable("UGUI_FIGMA_DEFAULT_SCREEN")
                                ?? EditorPrefs.GetString(EDITOR_PREF_DEFAULT_SCREEN, null);
            var targetPath = ResolveTargetScreenPath(prefabs, requestedName);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            var rtPrefab = prefab != null ? prefab.GetComponent<RectTransform>() : null;
            if (rtPrefab == null) { Debug.LogError("[UnityToFigmaBootstrap] prefab RectTransform 없음"); return; }

            int width = Mathf.Max(1, Mathf.RoundToInt(rtPrefab.rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rtPrefab.rect.height));
            var outputRel = ContextString("captureOutputPath")
                            ?? Environment.GetEnvironmentVariable("UGUI_FIGMA_CAPTURE_PATH")
                            ?? $"Assets/_Temp/{prefab.name}_Capture_{width}x{height}.png";
            CaptureSceneCanvases(width, height, outputRel, prefab.name);
        }

        // ----------------------------------------------------------------- TMP Korean Fallback
        //
        // 정책:
        // - 자동 *다운로드* 는 안 한다 (라이선스). 사용자가 폰트 파일만 프로젝트에 가져다 두면
        //   부트스트랩이 SDF 생성 + TMP_Settings.fallbackFontAssets 자동 등록까지 처리한다.
        // - SDF 는 AtlasPopulationMode.Dynamic 로 만든다. 한글 글리프가 런타임에 자동 생성된다.
        // - 같은 이름의 SDF 가 이미 있으면 재사용 (멱등).
        //
        // ContextFile 키:
        //   "koreanFontPath": "Assets/Fonts/NotoSansKR-Regular.ttf"      (선택; 없으면 자동 탐색)
        //   "koreanFontSdfOutputPath": "Assets/Fonts/NotoSansKR_SDF.asset" (선택; 기본 = 같은 폴더 + _SDF.asset)
        //
        // 자동 탐색 패턴 (koreanFontPath 미지정 시):
        //   Assets/Fonts/Noto*KR*.ttf, Assets/Fonts/Noto*KR*.otf, Assets/Fonts/*Korean*.ttf,
        //   Assets/Fonts/*Korean*.otf, Assets/Fonts/Pretendard*.ttf 등
        [MenuItem("Tools/UnityToFigma Bootstrap/Setup TMP Korean Fallback", priority = 24)]
        public static void SetupTmpKoreanFallback()
        {
            LoadContextFileIfPresent();
            var fontPath = ContextString("koreanFontPath") ?? AutoDiscoverKoreanFontPath();
            if (string.IsNullOrEmpty(fontPath))
            {
                Debug.LogWarning(
                    "[UnityToFigmaBootstrap] 한글 폰트 파일을 찾지 못했습니다. " +
                    "Assets/Fonts/ 에 NotoSansKR / Pretendard / NanumGothic 등 한글 글리프가 포함된 .ttf/.otf 를 두거나, " +
                    "ContextFile 에 \"koreanFontPath\": \"Assets/Path/Font.ttf\" 를 명시하세요.");
                return;
            }
            if (!File.Exists(fontPath))
            {
                Debug.LogError($"[UnityToFigmaBootstrap] 폰트 파일이 존재하지 않습니다: {fontPath}");
                return;
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogError($"[UnityToFigmaBootstrap] Font 로드 실패: {fontPath}. " +
                               ".ttf/.otf 가 Asset 으로 임포트되어 있는지 확인하세요.");
                return;
            }

            var outputPath = ContextString("koreanFontSdfOutputPath");
            if (string.IsNullOrEmpty(outputPath))
            {
                var dir = Path.GetDirectoryName(fontPath);
                var name = Path.GetFileNameWithoutExtension(fontPath);
                outputPath = $"{dir}/{name}_SDF.asset";
            }

            var fontAssetType = FindType("TMPro.TMP_FontAsset");
            if (fontAssetType == null)
            {
                Debug.LogError("[UnityToFigmaBootstrap] TMP_FontAsset 타입 미발견. TextMeshPro 패키지 확인.");
                return;
            }

            UnityEngine.Object sdf = AssetDatabase.LoadAssetAtPath(outputPath, fontAssetType);
            if (sdf == null)
            {
                sdf = CreateDynamicTmpFontAsset(fontAssetType, font);
                if (sdf == null)
                {
                    Debug.LogError("[UnityToFigmaBootstrap] TMP_FontAsset 생성 실패 (CreateFontAsset 시그니처 미발견).");
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                AssetDatabase.CreateAsset(sdf, outputPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[UnityToFigmaBootstrap] Korean SDF 생성: {outputPath} (Dynamic, source={Path.GetFileName(fontPath)})");
            }
            else
            {
                Debug.Log($"[UnityToFigmaBootstrap] Korean SDF 재사용: {outputPath}");
            }

            int registered = RegisterTmpGlobalFallback(sdf);
            int perAssetAdded = AddFallbackToImportedSdfs(sdf);
            Debug.Log($"[UnityToFigmaBootstrap] TMP Korean Fallback 적용 완료. " +
                      $"globalFallbackAdded={registered}, perAssetFallbackAdded={perAssetAdded}");
        }

        private static string AutoDiscoverKoreanFontPath()
        {
            // 1) 대표적인 한글 폰트 이름 우선
            string[] preferredPatterns = {
                "Assets/Fonts/NotoSansKR*.ttf",
                "Assets/Fonts/NotoSansKR*.otf",
                "Assets/Fonts/Noto*KR*.ttf",
                "Assets/Fonts/Noto*KR*.otf",
                "Assets/Fonts/Pretendard*.ttf",
                "Assets/Fonts/Pretendard*.otf",
                "Assets/Fonts/NanumGothic*.ttf",
                "Assets/Fonts/Nanum*.ttf",
                "Assets/Fonts/*Korean*.ttf",
                "Assets/Fonts/*Korean*.otf",
                "Assets/Fonts/*KR*.ttf",
                "Assets/Fonts/*KR*.otf",
            };
            string projectRoot = Directory.GetCurrentDirectory();
            foreach (var pattern in preferredPatterns)
            {
                var dir = Path.Combine(projectRoot, Path.GetDirectoryName(pattern));
                if (!Directory.Exists(dir)) continue;
                var matches = Directory.GetFiles(dir, Path.GetFileName(pattern), SearchOption.TopDirectoryOnly);
                if (matches.Length > 0)
                {
                    var rel = "Assets" + matches[0].Substring(Path.Combine(projectRoot, "Assets").Length).Replace('\\', '/');
                    return rel;
                }
            }
            return null;
        }

        private static UnityEngine.Object CreateDynamicTmpFontAsset(Type fontAssetType, Font font)
        {
            // CreateFontAsset(Font font, int samplingPointSize, int atlasPadding, GlyphRenderMode renderMode,
            //                 int atlasWidth, int atlasHeight, AtlasPopulationMode atlasPopulationMode,
            //                 bool enableMultiAtlasSupport)
            // 여러 시그니처를 reflection 으로 시도. 첫 매칭 사용.
            var renderModeType = FindType("UnityEngine.TextCore.LowLevel.GlyphRenderMode");
            var atlasModeType = FindType("TMPro.AtlasPopulationMode");

            if (renderModeType != null && atlasModeType != null)
            {
                var fullSig = fontAssetType.GetMethod("CreateFontAsset", BindingFlags.Static | BindingFlags.Public, null,
                    new[] { typeof(Font), typeof(int), typeof(int), renderModeType, typeof(int), typeof(int), atlasModeType, typeof(bool) }, null);
                if (fullSig != null)
                {
                    var sdfaa = TryGetEnumValue(renderModeType, "SDFAA"); // SDF + Anti-Aliased
                    var dynamicMode = TryGetEnumValue(atlasModeType, "Dynamic");
                    if (sdfaa != null && dynamicMode != null)
                    {
                        return (UnityEngine.Object)fullSig.Invoke(null, new object[]
                        {
                            font,         // font
                            90,           // samplingPointSize
                            9,            // atlasPadding
                            sdfaa,        // renderMode
                            1024, 1024,   // atlas size
                            dynamicMode,  // populationMode = Dynamic (한글 자동 글리프 생성)
                            true,         // enableMultiAtlasSupport
                        });
                    }
                }
            }

            // 폴백: 가장 단순한 (Font) 만 받는 시그니처
            var simple = fontAssetType.GetMethod("CreateFontAsset", BindingFlags.Static | BindingFlags.Public, null,
                new[] { typeof(Font) }, null);
            if (simple != null)
            {
                return (UnityEngine.Object)simple.Invoke(null, new object[] { font });
            }
            return null;
        }

        private static object TryGetEnumValue(Type enumType, string name)
        {
            try { return Enum.Parse(enumType, name); }
            catch { return null; }
        }

        private static int RegisterTmpGlobalFallback(UnityEngine.Object sdfFontAsset)
        {
            var settingsType = FindType("TMPro.TMP_Settings");
            if (settingsType == null) return 0;

            var instanceProp = settingsType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
            var instance = instanceProp?.GetValue(null) as UnityEngine.Object;
            if (instance == null) return 0;

            var listField = settingsType.GetField("m_fallbackFontAssets", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = listField?.GetValue(instance) as System.Collections.IList;
            if (list == null) return 0;

            foreach (var existing in list)
            {
                if (ReferenceEquals(existing, sdfFontAsset)) return 0;
            }
            list.Add(sdfFontAsset);
            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssetIfDirty(instance);
            return 1;
        }

        private static int AddFallbackToImportedSdfs(UnityEngine.Object sdfFontAsset)
        {
            var settings = LoadOrCreateSettingsAsset();
            var importRoot = settings != null ? GetStringField(settings, "ImportRoot") ?? "Assets/Figma" : "Assets/Figma";
            var fontsFolder = settings != null ? GetStringField(settings, "FontsFolderName") ?? "Fonts" : "Fonts";
            var sdfPaths = ListAssets($"{importRoot}/{fontsFolder}", "*.asset");
            int added = 0;
            var fontAssetType = FindType("TMPro.TMP_FontAsset");
            if (fontAssetType == null) return 0;
            foreach (var path in sdfPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath(path, fontAssetType);
                if (asset == null) continue;
                if (ReferenceEquals(asset, sdfFontAsset)) continue;

                var fallbackField = fontAssetType.GetField("m_FallbackFontAssetTable", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fallbackField == null) continue;
                var fallbackList = fallbackField.GetValue(asset) as System.Collections.IList;
                if (fallbackList == null) continue;

                bool already = false;
                foreach (var existing in fallbackList)
                {
                    if (ReferenceEquals(existing, sdfFontAsset)) { already = true; break; }
                }
                if (already) continue;
                fallbackList.Add(sdfFontAsset);
                EditorUtility.SetDirty(asset);
                added++;
            }
            if (added > 0) AssetDatabase.SaveAssets();
            return added;
        }

        private static string ResolveTargetScreenPath(List<string> prefabs, string requestedName)
        {
            string targetPath = null;
            if (!string.IsNullOrEmpty(requestedName))
            {
                foreach (var p in prefabs)
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(p), requestedName, StringComparison.OrdinalIgnoreCase))
                    { targetPath = p; break; }
                }
                // Suffix 폴백 (UnityToFigma 가 동일 이름 충돌 시 _1/_2 부여)
                if (targetPath == null)
                {
                    string best = null;
                    int bestIdx = int.MaxValue;
                    foreach (var p in prefabs)
                    {
                        var n = Path.GetFileNameWithoutExtension(p);
                        if (n.StartsWith(requestedName + "_", StringComparison.OrdinalIgnoreCase))
                        {
                            var tail = n.Substring(requestedName.Length + 1);
                            if (int.TryParse(tail, out var idx) && idx < bestIdx)
                            {
                                bestIdx = idx;
                                best = p;
                            }
                        }
                    }
                    targetPath = best;
                }
            }
            if (targetPath == null) targetPath = prefabs[0];
            return targetPath;
        }

        private static void CaptureSceneCanvases(int w, int h, string outputRel, string label)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string outputAbs = Path.IsPathRooted(outputRel) ? outputRel : Path.Combine(projectRoot, outputRel);
            string dir = Path.GetDirectoryName(outputAbs);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            if (canvases.Length == 0)
            {
                Debug.LogWarning("[UnityToFigmaBootstrap] 씬에 Canvas 가 없어 캡처를 스킵합니다.");
                return;
            }

            var origModes = new RenderMode[canvases.Length];
            var origCams = new Camera[canvases.Length];
            var scalers = new UnityEngine.UI.CanvasScaler[canvases.Length];
            var origScaleMode = new UnityEngine.UI.CanvasScaler.ScaleMode[canvases.Length];
            var origScaleFactor = new float[canvases.Length];
            var origRefRes = new Vector2[canvases.Length];
            var origMatch = new float[canvases.Length];

            var camGo = new GameObject("__UguiCaptureCam");
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
                scalers[i] = canvases[i].GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scalers[i] != null)
                {
                    origScaleMode[i] = scalers[i].uiScaleMode;
                    origScaleFactor[i] = scalers[i].scaleFactor;
                    origRefRes[i] = scalers[i].referenceResolution;
                    origMatch[i] = scalers[i].matchWidthOrHeight;

                    scalers[i].uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    scalers[i].scaleFactor = 1f;
                }
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = cam;
                canvases[i].planeDistance = 10;
            }

            try
            {
                Canvas.ForceUpdateCanvases();
                cam.Render();

                var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                File.WriteAllBytes(outputAbs, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
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
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(rt);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[UnityToFigmaBootstrap] 캡처 완료 ({label}, {w}x{h}) → {outputAbs}");
        }

        private static void ApplyGameViewAspect(int width, int height, string label)
        {
            try
            {
                var sizesAsm = typeof(UnityEditor.Editor).Assembly;
                var gvSizes = sizesAsm.GetType("UnityEditor.GameViewSizes");
                var groupTypeEnum = sizesAsm.GetType("UnityEditor.GameViewSizeGroupType");
                if (gvSizes == null || groupTypeEnum == null)
                {
                    Debug.LogWarning("[UnityToFigmaBootstrap] GameViewSizes 타입 미발견. GameView 종횡비 변경 생략.");
                    return;
                }
                var instProp = gvSizes.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
                if (instProp == null)
                {
                    Debug.LogWarning("[UnityToFigmaBootstrap] GameViewSizes.instance 프로퍼티 미발견.");
                    return;
                }
                var instance = instProp.GetValue(null);
                if (instance == null)
                {
                    Debug.LogWarning("[UnityToFigmaBootstrap] GameViewSizes.instance == null.");
                    return;
                }
                var getGroup = gvSizes.GetMethod("GetGroup", new[] { groupTypeEnum });
                if (getGroup == null)
                {
                    Debug.LogWarning("[UnityToFigmaBootstrap] GameViewSizes.GetGroup(groupType) 미발견.");
                    return;
                }
                var standalone = Enum.Parse(groupTypeEnum, "Standalone");
                var group = getGroup.Invoke(instance, new object[] { standalone });
                if (group == null)
                {
                    Debug.LogWarning("[UnityToFigmaBootstrap] GameViewSizeGroup(Standalone) 가져오기 실패.");
                    return;
                }
                var groupType = group.GetType();

                var sizeType = sizesAsm.GetType("UnityEditor.GameViewSize");
                var sizeTypeEnum = sizesAsm.GetType("UnityEditor.GameViewSizeType");
                var fixedRes = Enum.Parse(sizeTypeEnum, "FixedResolution");
                var displayName = $"UGUI {label} {width}x{height}";

                var customCount = (int)groupType.GetMethod("GetCustomCount").Invoke(group, null);
                int targetIndex = -1;
                var totalCount = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
                var getDisplayTexts = groupType.GetMethod("GetDisplayTexts");
                var labels = (string[])getDisplayTexts.Invoke(group, null);
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i].StartsWith(displayName))
                    {
                        targetIndex = i;
                        break;
                    }
                }
                if (targetIndex < 0)
                {
                    var sizeCtor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                    var newSize = sizeCtor.Invoke(new object[] { fixedRes, width, height, displayName });
                    groupType.GetMethod("AddCustomSize").Invoke(group, new[] { newSize });
                    labels = (string[])getDisplayTexts.Invoke(group, null);
                    for (int i = 0; i < labels.Length; i++)
                    {
                        if (labels[i].StartsWith(displayName)) { targetIndex = i; break; }
                    }
                }

                var gvType = sizesAsm.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gvType, false, null, false);
                var setSize = gvType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setSize != null)
                {
                    setSize.Invoke(gameView, new object[] { targetIndex, null });
                }
                else
                {
                    var selectedSizeIndex = gvType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (selectedSizeIndex != null) selectedSizeIndex.SetValue(gameView, targetIndex);
                }
                gameView.Repaint();
                Debug.Log($"[UnityToFigmaBootstrap] GameView 종횡비를 {displayName} 으로 설정 (index={targetIndex}).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UnityToFigmaBootstrap] GameView 종횡비 변경 실패: " + e.Message);
            }
        }

        private static void EditorSceneRefresh()
        {
            try
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            }
            catch { /* ignore */ }
        }

        // ----------------------------------------------------------------- Helpers

        private static void LoadContextFileIfPresent()
        {
            s_Context = null;
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), CONTEXT_FILE_RELATIVE);
                if (!File.Exists(path)) return;
                var raw = File.ReadAllText(path);
                s_Context = ParseSimpleJson(raw);
                Debug.Log("[UnityToFigmaBootstrap] context 파일 로드: " + path);
                // ContextFile 은 sync 완료 시 자동 삭제되지만, defaultScreenName 등은
                // 후속 메뉴 (Instantiate Default Screen) 가 다시 사용해야 하므로 EditorPrefs 로 영속화한다.
                PersistContextDefaults();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UnityToFigmaBootstrap] context 파일 파싱 실패: " + e.Message);
            }
        }

        private static void PersistContextDefaults()
        {
            if (s_Context == null) return;
            var defaultScreen = ContextString("defaultScreenName");
            if (!string.IsNullOrEmpty(defaultScreen))
            {
                EditorPrefs.SetString(EDITOR_PREF_DEFAULT_SCREEN, defaultScreen);
            }
            var url = ContextString("documentUrl");
            if (!string.IsNullOrEmpty(url))
            {
                EditorPrefs.SetString(EDITOR_PREF_URL, url);
            }
            var pat = ContextString("pat");
            if (!string.IsNullOrEmpty(pat))
            {
                EditorPrefs.SetString(EDITOR_PREF_PAT, pat);
            }
        }

        private static void DeleteContextFileUnlessKept()
        {
            if (s_Context == null) return;
            if (s_Context.TryGetValue("keepContext", out var keep) && keep is bool b && b) return;
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), CONTEXT_FILE_RELATIVE);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore */ }
        }

        private static string ContextString(string key)
        {
            if (s_Context == null) return null;
            if (!s_Context.TryGetValue(key, out var v)) return null;
            return v as string;
        }

        private static bool? ContextBool(string key)
        {
            if (s_Context == null) return null;
            if (!s_Context.TryGetValue(key, out var v)) return null;
            if (v is bool b) return b;
            if (v is string s)
            {
                if (bool.TryParse(s, out var bb)) return bb;
            }
            return null;
        }

        // ContextFile 의 string 배열 (또는 단일 string) 을 List<string> 으로 정규화한다.
        // - "selectedPages": ["Page 1", "Page 3"]   → ["Page 1", "Page 3"]
        // - "selectedPages": "Page 1"               → ["Page 1"] (편의)
        // - 누락/null/빈 배열                        → null
        private static List<string> ContextStringList(string key)
        {
            if (s_Context == null) return null;
            if (!s_Context.TryGetValue(key, out var v) || v == null) return null;
            if (v is string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                return new List<string> { s };
            }
            if (v is List<object> list)
            {
                var result = new List<string>();
                foreach (var item in list)
                {
                    if (item is string str && !string.IsNullOrWhiteSpace(str)) result.Add(str);
                }
                return result.Count == 0 ? null : result;
            }
            return null;
        }

        private static double? ContextDouble(string key)
        {
            if (s_Context == null) return null;
            if (!s_Context.TryGetValue(key, out var v)) return null;
            if (v is double d) return d;
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is string s && double.TryParse(s, out var dd)) return dd;
            return null;
        }

        private static string ResolveDocumentUrl()
        {
            var c = ContextString("documentUrl");
            if (!string.IsNullOrEmpty(c)) return c;
            var env = Environment.GetEnvironmentVariable("FIGMA_DOCUMENT_URL");
            if (!string.IsNullOrEmpty(env)) return env;
            var pref = EditorPrefs.GetString(EDITOR_PREF_URL, string.Empty);
            if (!string.IsNullOrEmpty(pref)) return pref;
            // 마지막 폴백: 이미 존재하는 UnityToFigmaSettings.DocumentUrl
            var settings = TryFindExistingSettings();
            if (settings != null)
            {
                var existing = GetStringField(settings, "DocumentUrl");
                if (!string.IsNullOrEmpty(existing)) return existing;
            }
            return string.Empty;
        }

        private static UnityEngine.Object TryFindExistingSettings()
        {
            var t = FindUnityToFigmaSettingsType();
            if (t == null) return null;
            var guids = AssetDatabase.FindAssets("t:" + t.Name);
            if (guids == null || guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), t);
        }

        private static string ResolvePat()
        {
            var c = ContextString("pat");
            if (!string.IsNullOrEmpty(c)) return c;
            var env = Environment.GetEnvironmentVariable("FIGMA_PAT");
            if (!string.IsNullOrEmpty(env)) return env;
            var pref = EditorPrefs.GetString(EDITOR_PREF_PAT, string.Empty);
            if (!string.IsNullOrEmpty(pref)) return pref;
            return PlayerPrefs.GetString(FIGMA_PAT_PREF_KEY, string.Empty);
        }

        private static bool ResolveBuildPrototypeFlow()
        {
            var c = ContextBool("buildPrototypeFlow");
            if (c.HasValue) return c.Value;
            var env = Environment.GetEnvironmentVariable("UGUI_FIGMA_BUILD_PROTOTYPE_FLOW");
            return string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveReportPath()
        {
            var c = ContextString("reportPath");
            if (!string.IsNullOrEmpty(c)) return c;
            var env = Environment.GetEnvironmentVariable("UGUI_FIGMA_REPORT_PATH");
            return string.IsNullOrEmpty(env) ? DEFAULT_REPORT_PATH : env;
        }

        private static double ResolveSyncTimeoutSecondsFromContext()
        {
            var c = ContextDouble("syncTimeoutSeconds");
            if (c.HasValue && c.Value > 0) return c.Value;
            return ResolveSyncTimeoutSeconds();
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
            return FindType("UnityToFigma.Editor.Settings.UnityToFigmaSettings");
        }

        private static void ApplySettings(UnityEngine.Object settings, string url)
        {
            SetField(settings, "DocumentUrl", url);

            // BuildPrototypeFlow 강제 false (다이얼로그 회피). context/env 로 명시적으로 켤 수 있음.
            SetField(settings, "BuildPrototypeFlow", ResolveBuildPrototypeFlow());

            // 페이지 선택 모드: ContextFile.selectedPages 가 있으면 보존, 없으면 끈다.
            // (PreparePageSelection 메뉴가 선행되어 PageDataList 와 OnlyImportSelectedPages 를 미리 설정한 상태.
            //  여기서 false 로 덮어버리면 직전 PageSelection 결과가 무효화된다.)
            var selectedPages = ContextStringList("selectedPages");
            if (selectedPages == null || selectedPages.Count == 0)
            {
                SetField(settings, "OnlyImportSelectedPages", false);
            }
            // selectedPages 가 있으면 OnlyImportSelectedPages 와 PageDataList 는 그대로 유지.

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

        // 라운드 코너 스캔.
        //
        // 핵심: UnityToFigma 의 FigmaImage 컴포넌트는 SDF 기반 cornerRadius 를 자체 처리한다 (Path A 한정).
        //   - handled : cornerRadius != 0 (UnityToFigma 가 정상 처리한 항목, 검수용 정보)
        //   - extreme : pill/circle 후보 (cornerRadius 가 999 등 극단값 → 시각 검토 권장)
        //   - skipped : FigmaImage 컴포넌트는 있는데 cornerRadius 를 읽지 못한 경우 (드물지만 보고)
        //
        // 본 스킬은 어떤 경우에도 라운드 코너를 자동으로 "보정/생성/스프라이트 대체" 하지 않는다.
        // 이 리포트는 단지 "어떤 노드가 라운드인지" 사용자/에이전트가 확인하기 위한 정보일 뿐이다.
        private static Dictionary<string, object> ScanRounded(List<string> screenPaths)
        {
            var handled = new List<object>();
            var extreme = new List<object>();
            var skipped = new List<object>();
            var result = new Dictionary<string, object>
            {
                ["handled"] = handled,
                ["extreme"] = extreme,
                ["skipped"] = skipped,
            };

            if (screenPaths == null || screenPaths.Count == 0) return result;

            var figmaImageType = FindType("UnityToFigma.Runtime.UI.FigmaImage");
            if (figmaImageType == null)
            {
                // FigmaImage 타입을 못 찾으면 라운드 검출 불가 → 사용자에게 알림용 마커
                skipped.Add(new Dictionary<string, object>
                {
                    ["reason"] = "FigmaImage 타입을 찾을 수 없음 (UnityToFigma Runtime 미참조 추정).",
                });
                return result;
            }
            var radiusField = figmaImageType.GetField("m_CornerRadius",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (radiusField == null)
            {
                skipped.Add(new Dictionary<string, object>
                {
                    ["reason"] = "FigmaImage.m_CornerRadius 필드를 찾을 수 없음 (패키지 구조 변경 추정).",
                });
                return result;
            }

            foreach (var prefabPath in screenPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                var components = prefab.GetComponentsInChildren(figmaImageType, true);
                foreach (var c in components)
                {
                    if (c == null) continue;
                    Vector4 r;
                    try
                    {
                        var v = radiusField.GetValue(c);
                        if (v is Vector4 vv) r = vv;
                        else continue;
                    }
                    catch
                    {
                        continue;
                    }

                    if (r.x == 0f && r.y == 0f && r.z == 0f && r.w == 0f) continue;

                    var node = c as Component;
                    var path = node != null ? GetTransformPath(node.transform) : "?";
                    var maxR = Mathf.Max(Mathf.Max(r.x, r.y), Mathf.Max(r.z, r.w));
                    var entry = new Dictionary<string, object>
                    {
                        ["prefab"] = prefabPath,
                        ["path"] = path,
                        ["cornerRadius"] = new[] { r.x, r.y, r.z, r.w },
                    };

                    if (maxR >= 500f)
                    {
                        entry["reason"] = "extreme-radius (pill/circle 후보)";
                        extreme.Add(entry);
                    }
                    else
                    {
                        handled.Add(entry);
                    }
                }
            }

            return result;
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

        // 평면 JSON 객체만 지원: {"key": "string" | number | bool | null}
        // 중첩 객체/배열은 무시한다 (현재 컨텍스트 파일 스키마는 모두 스칼라).
        private static Dictionary<string, object> ParseSimpleJson(string text)
        {
            var dict = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(text)) return dict;
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{') return dict;
            i++;
            while (i < text.Length)
            {
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == '}') { i++; break; }
                var key = ReadString(text, ref i);
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != ':') break;
                i++;
                SkipWs(text, ref i);
                var value = ReadValue(text, ref i);
                if (key != null) dict[key] = value;
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ',') { i++; continue; }
                if (i < text.Length && text[i] == '}') { i++; break; }
            }
            return dict;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
        }

        private static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return null;
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i < s.Length)
                {
                    var esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(esc); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static object ReadValue(string s, ref int i)
        {
            if (i >= s.Length) return null;
            var c = s[i];
            if (c == '"') return ReadString(s, ref i);
            if (c == 't' || c == 'f')
            {
                if (i + 4 <= s.Length && s.Substring(i, 4) == "true") { i += 4; return true; }
                if (i + 5 <= s.Length && s.Substring(i, 5) == "false") { i += 5; return false; }
            }
            if (c == 'n' && i + 4 <= s.Length && s.Substring(i, 4) == "null") { i += 4; return null; }
            // 숫자
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int start = i;
                while (i < s.Length && "0123456789.-+eE".IndexOf(s[i]) >= 0) i++;
                var raw = s.Substring(start, i - start);
                if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d;
                return raw;
            }
            // 중첩 객체/배열은 스킵 (단순 토큰 컨슘)
            if (c == '{')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    if (s[i] == '{') depth++;
                    else if (s[i] == '}') { depth--; if (depth == 0) { i++; break; } }
                    i++;
                }
                return null;
            }
            if (c == '[')
            {
                // 배열은 List<object> 로 채워준다 (selectedPages, koreanFontFallbackTargets 등 string 배열 지원).
                i++;
                var list = new List<object>();
                while (i < s.Length)
                {
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; break; }
                    var element = ReadValue(s, ref i);
                    list.Add(element);
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; break; }
                }
                return list;
            }
            i++;
            return null;
        }
    }
}
#endif
