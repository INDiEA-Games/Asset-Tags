#if UNITY_2021_2_OR_NEWER
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Search;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace INDiEA.AssetTags
{
    [InitializeOnLoad]
    static class AssetTagsProjectBrowserToolbar
    {
        static class ProjectBrowserIconTooltips
        {
            public const string SearchByType = "Search by Type";
            public const string SearchByLabel = "Search by Label";
            public const string SearchByAssetTag = "Search by Asset Tag";
        }

        const string OpenInSearchShortcutPath = "Main Menu/Edit/Search/Search All...";
        const string OverlayElementName = "INDiEA-AssetTags-ProjectToolbarOverlay";
        const string FirstRunDialogPrefKey = "INDiEA.AssetTags.ProjectToolbarOverlay.Warned";
        const float OverlayToolbarHeight = 22f;

        static readonly Type projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
        static bool harmonyTopToolbarTried;
        static bool harmonyTopToolbarPatched;

        static MethodInfo isTwoColumnsMethod;
        static MethodInfo createDropdownMethod;
        static MethodInfo searchFieldMethod;
        static MethodInfo assetLabelsDropDownMethod;
        static MethodInfo typeDropDownMethod;
        static MethodInfo logTypeDropDownMethod;
        static MethodInfo buttonSaveFilterMethod;
        static MethodInfo toggleHiddenPackagesMethod;
        static FieldInfo searchFieldTextField;
        static FieldInfo directoriesAreaWidthField;
        static bool forceNativeToolbarFallback;

        static readonly Dictionary<int, string> searchTextByBrowser = new Dictionary<int, string>();

        static AssetTagsSettings settingsCache;
        static int injectTick;
        static int forceTryInject;

        static readonly List<(Rect r, string tip)> toolbarTooltipHits = new List<(Rect, string)>(24);

        static MethodInfo tooltipViewShowMethod;
        static MethodInfo tooltipViewForceCloseMethod;

        static string activeTooltipText;
        static Rect activeTooltipAnchor;
        static string lastShownTooltipText;
        static Rect lastShownTooltipAnchor;
        static bool lastShownTooltipValid;

        static GUIStyle createToolbarStyle;
        static GUIStyle searchJumpButtonStyle;
        static bool warnedTopToolbarPatchFailure;
        static bool warnedToolbarReflectionFallback;

        static AssetTagsProjectBrowserToolbar()
        {
            if (projectBrowserType == null)
                return;

            EditorApplication.projectChanged += InvalidateSettingsCache;

            isTwoColumnsMethod = projectBrowserType.GetMethod(
                "IsTwoColumns",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            createDropdownMethod = projectBrowserType.GetMethod(
                "CreateDropdown",
                BindingFlags.Instance | BindingFlags.NonPublic);

            searchFieldMethod = projectBrowserType.GetMethod(
                "SearchField",
                BindingFlags.Instance | BindingFlags.NonPublic);

            assetLabelsDropDownMethod = projectBrowserType.GetMethod(
                "AssetLabelsDropDown",
                BindingFlags.Instance | BindingFlags.NonPublic);

            buttonSaveFilterMethod = projectBrowserType.GetMethod(
                "ButtonSaveFilter",
                BindingFlags.Instance | BindingFlags.NonPublic);

            typeDropDownMethod = projectBrowserType.GetMethod(
                "TypeDropDown",
                BindingFlags.Instance | BindingFlags.NonPublic);

#if UNITY_2022_3_OR_NEWER
            logTypeDropDownMethod = GetProjectBrowserMethodByCandidates(
                "ImportLogTypeDropDown",
                "SearchByImportLogTypeDropDown",
                "ImportActivityDropDown",
                "LogTypeDropDown");
#else
            logTypeDropDownMethod = null;
#endif

            toggleHiddenPackagesMethod = projectBrowserType.GetMethod(
                "ToggleHiddenPackagesVisibility",
                BindingFlags.Instance | BindingFlags.NonPublic);

            searchFieldTextField = projectBrowserType.GetField(
                "m_SearchFieldText",
                BindingFlags.Instance | BindingFlags.NonPublic);

            directoriesAreaWidthField = projectBrowserType.GetField(
                "m_DirectoriesAreaWidth",
                BindingFlags.Instance | BindingFlags.NonPublic);

            TryInstallTopToolbarHarmonyPatchSafe();
            EditorApplication.update += TryInject;
        }

        public static void InvalidateSettingsCache()
        {
            settingsCache = null;
            forceTryInject = 1;
            RemoveToolbarOverlays();
        }

        static bool ShouldSkipNativeProjectTopToolbar() =>
            harmonyTopToolbarPatched && IsProjectBrowserToolbarEnabled();

        static void TryInstallTopToolbarHarmonyPatchSafe()
        {
            if (harmonyTopToolbarTried)
                return;
            harmonyTopToolbarTried = true;

            try
            {
                var topToolbar = projectBrowserType.GetMethod(
                    "TopToolbar",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (topToolbar == null)
                {
                    LogDebugWarningOnce(
                        ref warnedTopToolbarPatchFailure,
                        "[AssetTags] Failed to patch ProjectBrowser.TopToolbar: method was not found. Toolbar overlay will be disabled.");
                    return;
                }

                var prefix = typeof(AssetTagsProjectBrowserToolbar).GetMethod(
                    nameof(HarmonyPrefix_ProjectBrowserTopToolbar),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                {
                    LogDebugWarningOnce(
                        ref warnedTopToolbarPatchFailure,
                        "[AssetTags] Failed to patch ProjectBrowser.TopToolbar: prefix method was not found.");
                    return;
                }

                var harmonyType =
                    Type.GetType("HarmonyLib.Harmony, 0Harmony")
                    ?? Type.GetType("HarmonyLib.Harmony, HarmonyLib");
                var harmonyMethodType =
                    Type.GetType("HarmonyLib.HarmonyMethod, 0Harmony")
                    ?? Type.GetType("HarmonyLib.HarmonyMethod, HarmonyLib");
                if (harmonyType == null || harmonyMethodType == null)
                {
                    LogDebugWarningOnce(
                        ref warnedTopToolbarPatchFailure,
                        "[AssetTags] Harmony was not found. Project toolbar patch is disabled.");
                    return;
                }

                var harmonyCtor = harmonyType.GetConstructor(new[] { typeof(string) });
                var harmonyMethodCtor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
                var patchMethod = harmonyType.GetMethod(
                    "Patch",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(MethodBase), harmonyMethodType, harmonyMethodType, harmonyMethodType, harmonyMethodType },
                    null);
                if (harmonyCtor == null || harmonyMethodCtor == null || patchMethod == null)
                {
                    LogDebugWarningOnce(
                        ref warnedTopToolbarPatchFailure,
                        "[AssetTags] Harmony patch API was not found. Project toolbar patch is disabled.");
                    return;
                }

                var harmony = harmonyCtor.Invoke(new object[] { "com.indiea.assettags.projectbrowser.toptoolbar" });
                var prefixHarmonyMethod = harmonyMethodCtor.Invoke(new object[] { prefix });
                patchMethod.Invoke(harmony, new[] { topToolbar, prefixHarmonyMethod, null, null, null });

                harmonyTopToolbarPatched = true;
            }
            catch (Exception exception)
            {
                harmonyTopToolbarPatched = false;
                LogDebugWarningOnce(
                    ref warnedTopToolbarPatchFailure,
                    "[AssetTags] Exception while patching ProjectBrowser.TopToolbar. Falling back to native toolbar.",
                    exception);
            }
        }

        static bool HarmonyPrefix_ProjectBrowserTopToolbar() => !ShouldSkipNativeProjectTopToolbar();

        static void EnsureTooltipViewReflected()
        {
            if (tooltipViewShowMethod != null)
                return;

            var tv = typeof(EditorApplication).Assembly.GetType("UnityEditor.TooltipView");
            if (tv == null)
                return;

            foreach (var m in tv.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "Show")
                    continue;
                var ps = m.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(Rect))
                {
                    tooltipViewShowMethod = m;
                    break;
                }
            }

            tooltipViewForceCloseMethod = tv.GetMethod(
                "ForceClose",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        static void ToolbarTooltipForceClose()
        {
            try
            {
                EnsureTooltipViewReflected();
                tooltipViewForceCloseMethod?.Invoke(null, null);
            }
            catch
            {
            }
        }

        static void ClearCustomToolbarTooltipSchedulingOnly()
        {
            EditorApplication.delayCall -= ApplyToolbarTooltipShowNow;
            activeTooltipText = null;
            lastShownTooltipValid = false;
        }

        static void ClearActiveToolbarTooltipState()
        {
            ClearCustomToolbarTooltipSchedulingOnly();
            ToolbarTooltipForceClose();
        }

        static bool ToolbarTooltipScreenAnchorNearlySame(Rect a, Rect b, float eps = 1f) =>
            Mathf.Abs(a.x - b.x) <= eps &&
            Mathf.Abs(a.y - b.y) <= eps &&
            Mathf.Abs(a.width - b.width) <= eps &&
            Mathf.Abs(a.height - b.height) <= eps;

        static void ApplyToolbarTooltipShowNow()
        {
            if (!IsProjectBrowserToolbarEnabled())
                return;
            if (string.IsNullOrEmpty(activeTooltipText))
                return;

            EnsureTooltipViewReflected();
            if (tooltipViewShowMethod == null)
                return;

            if (lastShownTooltipValid &&
                lastShownTooltipText == activeTooltipText &&
                ToolbarTooltipScreenAnchorNearlySame(lastShownTooltipAnchor, activeTooltipAnchor))
            {
                return;
            }

            try
            {
                tooltipViewShowMethod.Invoke(
                    null,
                    new object[] { activeTooltipText, activeTooltipAnchor, null });
                lastShownTooltipText = activeTooltipText;
                lastShownTooltipAnchor = activeTooltipAnchor;
                lastShownTooltipValid = true;
            }
            catch
            {
            }
        }

        static void PushToolbarTooltipHit(Rect r, string tip)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            toolbarTooltipHits.Add((r, tip ?? string.Empty));
        }

        static void FinishToolbarTooltipHitsForFrame(float stripW, float stripHeight)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var stripGui = new Rect(0f, 0f, stripW, stripHeight);
            if (!stripGui.Contains(Event.current.mousePosition))
            {
                toolbarTooltipHits.Clear();
                ClearActiveToolbarTooltipState();
                return;
            }

            string tip = null;
            Rect? hitRect = null;
            for (var i = toolbarTooltipHits.Count - 1; i >= 0; i--)
            {
                var entry = toolbarTooltipHits[i];
                if (entry.r.Contains(Event.current.mousePosition))
                {
                    tip = entry.tip;
                    hitRect = entry.r;
                    break;
                }
            }

            toolbarTooltipHits.Clear();

            if (string.IsNullOrEmpty(tip) || hitRect == null)
            {
                ClearActiveToolbarTooltipState();
                return;
            }

            var anchor = EditorGUIUtility.GUIToScreenRect(hitRect.Value);
            anchor = new Rect(
                Mathf.Round(anchor.x),
                Mathf.Round(anchor.y),
                Mathf.Round(anchor.width),
                Mathf.Round(anchor.height));
            activeTooltipText = tip;
            activeTooltipAnchor = anchor;

            ApplyToolbarTooltipShowNow();
        }

        static bool IsProjectBrowserToolbarEnabled()
        {
            EnsureSettingsLoaded();
            if (settingsCache == null)
                return true;
            if ((UnityEngine.Object)settingsCache == null)
            {
                InvalidateSettingsCache();
                EnsureSettingsLoaded();
                if (settingsCache == null)
                    return true;
            }
            return settingsCache.OverrideProjectBrowserToolbar;
        }

        static bool IsDebugLogEnabled()
        {
            EnsureSettingsLoaded();
            return settingsCache != null && settingsCache.EnableDebugLogs;
        }

        static void LogDebugWarningOnce(ref bool warned, string message, Exception exception = null)
        {
            if (warned || !IsDebugLogEnabled())
                return;
            warned = true;
            if (exception == null)
                Debug.LogWarning(message);
            else
                Debug.LogWarning($"{message}\n{exception}");
        }

        static void EnsureSettingsLoaded()
        {
            if (settingsCache != null)
                return;

            AssetTagsManager.EnsureCoreAssetsExist();
            settingsCache = AssetDatabase.LoadAssetAtPath<AssetTagsSettings>(AssetTagsManager.SettingsAssetPath);
        }

        static void RemoveOverlaysByName(string visualElementName)
        {
            if (projectBrowserType == null)
                return;

            var browsers = Resources.FindObjectsOfTypeAll(projectBrowserType);
            for (var i = 0; i < browsers.Length; i++)
            {
                var w = browsers[i] as EditorWindow;
                var root = w?.rootVisualElement;
                if (root == null)
                    continue;

                var q = root.Q<VisualElement>(visualElementName);
                if (q != null)
                {
                    q.RemoveFromHierarchy();
                    continue;
                }

                for (var c = root.childCount - 1; c >= 0; c--)
                {
                    var child = root.ElementAt(c);
                    if (child != null && child.name == visualElementName)
                        child.RemoveFromHierarchy();
                }
            }
        }

        static bool AnyProjectBrowserHasToolbarOverlay()
        {
            if (projectBrowserType == null)
                return false;

            var browsers = Resources.FindObjectsOfTypeAll(projectBrowserType);
            for (var i = 0; i < browsers.Length; i++)
            {
                var w = browsers[i] as EditorWindow;
                var root = w?.rootVisualElement;
                if (root == null)
                    continue;
                if (root.Q<VisualElement>(OverlayElementName) != null)
                    return true;
                for (var c = 0; c < root.childCount; c++)
                {
                    var child = root.ElementAt(c);
                    if (child != null && child.name == OverlayElementName)
                        return true;
                }
            }

            return false;
        }

        static void RemoveToolbarOverlays()
        {
            var hadOverlay = AnyProjectBrowserHasToolbarOverlay();
            RemoveOverlaysByName(OverlayElementName);
            if (hadOverlay)
                ClearActiveToolbarTooltipState();
            else
                ClearCustomToolbarTooltipSchedulingOnly();
        }

        static void TryInject()
        {
            if (projectBrowserType == null)
                return;

            var force = forceTryInject != 0;
            if (force)
                forceTryInject = 0;
            else if ((++injectTick % 20) != 0)
                return;

            if (!IsProjectBrowserToolbarEnabled())
            {
                RemoveToolbarOverlays();
                return;
            }

            if (!harmonyTopToolbarPatched)
            {
                RemoveToolbarOverlays();
                return;
            }

            var browsers = Resources.FindObjectsOfTypeAll(projectBrowserType);
            for (var i = 0; i < browsers.Length; i++)
            {
                var w = browsers[i] as EditorWindow;
                if (w == null)
                    continue;

                var root = w.rootVisualElement;
                if (root == null)
                    continue;

                if (root.Q<VisualElement>(OverlayElementName) != null)
                    continue;

                var captured = w;
                var ve = new IMGUIContainer(() => DrawProjectToolbarOverlay(captured)) { name = OverlayElementName };
                ve.style.position = Position.Absolute;
                ve.style.left = 0;
                ve.style.right = 0;
                ve.style.top = 0;
                ve.style.height = OverlayToolbarHeight;
                ve.pickingMode = PickingMode.Position;
                ve.style.backgroundColor = ToolbarStripBackgroundColor();
                ve.RegisterCallback<AttachToPanelEvent>(_ => ve.BringToFront());
                root.Add(ve);
                ve.BringToFront();
            }
        }

        static Color ToolbarStripBackgroundColor() =>
            EditorGUIUtility.isProSkin
                ? new Color(0.195f, 0.195f, 0.195f, 1f)
                : new Color(0.635f, 0.635f, 0.635f, 1f);

        static void DrawProjectToolbarOverlay(EditorWindow projectBrowser)
        {
            if (projectBrowser == null || projectBrowserType == null)
                return;

            if (Event.current.type == EventType.Repaint)
                toolbarTooltipHits.Clear();

            if (!EditorPrefs.GetBool(FirstRunDialogPrefKey, false))
            {
                EditorPrefs.SetBool(FirstRunDialogPrefKey, true);
                EditorApplication.delayCall += () =>
                    EditorUtility.DisplayDialog(
                        "INDiEA Project Toolbar",
                        "The project toolbar is replaced using Harmony. If Harmony is missing or patching fails, Unity's default toolbar remains active.",
                        "OK");
            }

            var listWidth = projectBrowser.position.width - GetDirectoriesAreaWidth(projectBrowser);
            var compact = listWidth < 500f;
            var spaceBetween = compact ? 4f : 10f;

            var stripW = projectBrowser.position.width;

            GUILayout.BeginArea(new Rect(0f, 0f, stripW, OverlayToolbarHeight));
            try
            {
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(0f, 0f, stripW, OverlayToolbarHeight), ToolbarStripBackgroundColor());

                GUILayout.BeginHorizontal(EditorStyles.toolbar);

                if (CanUseNativeReflectedToolbar())
                {
                    DrawCreateMenu(projectBrowser);
                    GUILayout.FlexibleSpace();
                    GUILayout.Space(spaceBetween * 2f);

                    DrawSearchBar(projectBrowser);
                    DrawSearchByTypeMenu(projectBrowser);
                    DrawSearchByLabelMenu(projectBrowser);
                    DrawIndieaAssetTagsMenu();
#if UNITY_2022_3_OR_NEWER
                    DrawLogTypeMenu(projectBrowser);
#endif
                    if (IsTwoColumns(projectBrowser))
                        DrawSaveSearch(projectBrowser);
                    DrawHiddenPackagesToggle(projectBrowser);
                }
                else
                {
                    DrawCreateMenuFallback(projectBrowser);
                    GUILayout.FlexibleSpace();
                    GUILayout.Space(spaceBetween * 2f);

                    DrawSearchBarFallback(projectBrowser);
                    DrawOpenInSearchButton();
                    DrawSearchByTypeMenu(projectBrowser);
                    DrawSearchByLabelMenu(projectBrowser);
                    DrawIndieaAssetTagsMenu();
#if UNITY_2022_3_OR_NEWER
                    DrawLogTypeMenu(projectBrowser);
#endif
                    if (IsTwoColumns(projectBrowser))
                        DrawSaveSearchFallback(projectBrowser);
                    DrawHiddenPackagesToggleFallback(projectBrowser);
                }

                GUILayout.EndHorizontal();

                FinishToolbarTooltipHitsForFrame(stripW, OverlayToolbarHeight);

                GUI.tooltip = string.Empty;
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        static bool InvokeProjectBrowserToolbarMethod(EditorWindow projectBrowser, MethodInfo method)
        {
            if (forceNativeToolbarFallback)
                return false;
            if (projectBrowser == null || method == null)
                return false;

            try
            {
                method.Invoke(projectBrowser, null);
                return true;
            }
            catch (TargetInvocationException tie)
            {
                if (tie.InnerException is ExitGUIException exitGui)
                    throw exitGui;
                forceNativeToolbarFallback = true;
                LogDebugWarningOnce(
                    ref warnedToolbarReflectionFallback,
                    "[AssetTags] Toolbar reflection call failed. Falling back to simplified toolbar.",
                    tie.InnerException ?? tie);
                return false;
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception exception)
            {
                forceNativeToolbarFallback = true;
                LogDebugWarningOnce(
                    ref warnedToolbarReflectionFallback,
                    "[AssetTags] Toolbar reflection call failed. Falling back to simplified toolbar.",
                    exception);
                return false;
            }
        }

        static bool CanUseNativeReflectedToolbar()
        {
            if (forceNativeToolbarFallback)
                return false;

            return createDropdownMethod != null &&
                searchFieldMethod != null &&
                typeDropDownMethod != null &&
                assetLabelsDropDownMethod != null &&
                buttonSaveFilterMethod != null &&
                toggleHiddenPackagesMethod != null;
        }

        static MethodInfo GetProjectBrowserMethodByCandidates(params string[] methodNames)
        {
            if (projectBrowserType == null || methodNames == null)
                return null;

            for (var i = 0; i < methodNames.Length; i++)
            {
                var methodName = methodNames[i];
                if (string.IsNullOrEmpty(methodName))
                    continue;

                var method = projectBrowserType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method != null)
                    return method;
            }

            return null;
        }

        static GUIContent CloneIconContent(GUIContent source)
        {
            if (source == null)
                return new GUIContent();
            return new GUIContent
            {
                image = source.image as Texture,
                text = source.text ?? string.Empty,
                tooltip = source.tooltip ?? string.Empty,
            };
        }

        static string BuildOpenInSearchTooltip()
        {
            try
            {
                if (ShortcutManager.instance != null)
                {
                    var binding = ShortcutManager.instance.GetShortcutBinding(OpenInSearchShortcutPath);
                    if (!binding.Equals(ShortcutBinding.empty))
                        return $"Open in Search ({binding})";
                }
            }
            catch
            {
            }

            return "Open in Search";
        }

        static GUIStyle ToolbarSearchJumpButtonStyleOrFallback()
        {
            if (searchJumpButtonStyle != null)
                return searchJumpButtonStyle;

            var p = typeof(EditorStyles).GetProperty(
                "toolbarSearchFieldJumpButton",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            searchJumpButtonStyle = p?.GetValue(null) as GUIStyle;
            return searchJumpButtonStyle != null ? searchJumpButtonStyle : EditorStyles.toolbarButton;
        }

        static GUIStyle CreateToolbarDropDownStyle()
        {
            if (createToolbarStyle != null)
                return createToolbarStyle;
            createToolbarStyle = GUI.skin.FindStyle("ToolbarCreateAddNewDropDown");
            if (createToolbarStyle == null)
                createToolbarStyle = EditorStyles.toolbarButton;
            return createToolbarStyle;
        }

        static void DrawCreateMenu(EditorWindow browser)
        {
            if (InvokeProjectBrowserToolbarMethod(browser, createDropdownMethod))
                return;

            DrawCreateMenuFallback(browser);
        }

        static void DrawCreateMenuFallback(EditorWindow _)
        {
            var isReadOnly =
                Selection.activeObject != null &&
                string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Selection.activeObject));

            using (new EditorGUI.DisabledScope(isReadOnly))
            {
                var content = CloneIconContent(EditorGUIUtility.IconContent("CreateAddNew"));
                var style = CreateToolbarDropDownStyle();
                var r = GUILayoutUtility.GetRect(content, style);
                if (EditorGUI.DropdownButton(r, content, FocusType.Passive, style))
                {
                    GUIUtility.hotControl = 0;
                    EditorUtility.DisplayPopupMenu(r, "Assets/Create", null);
                }

                PushToolbarTooltipHit(r, content.tooltip);
            }
        }

        static string ReadNativeSearchFieldText(EditorWindow projectBrowser)
        {
            if (searchFieldTextField == null)
                return string.Empty;
            try
            {
                return searchFieldTextField.GetValue(projectBrowser) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        static void ApplySearchQuery(EditorWindow projectBrowser, string query)
        {
            var id = projectBrowser.GetInstanceID();
            searchTextByBrowser[id] = query ?? string.Empty;
            AssetTagsProjectBrowserSearch.SetProjectSearch(searchTextByBrowser[id]);
        }

        static void DrawSearchBar(EditorWindow projectBrowser)
        {
            if (InvokeProjectBrowserToolbarMethod(projectBrowser, searchFieldMethod))
                return;

            DrawSearchBarFallback(projectBrowser);
        }

        static void DrawSearchBarFallback(EditorWindow projectBrowser)
        {
            var id = projectBrowser.GetInstanceID();
            searchTextByBrowser[id] = ReadNativeSearchFieldText(projectBrowser);

            var labelMax = EditorGUIUtility.labelWidth > 1f ? EditorGUIUtility.labelWidth : 170f;
            var rect = GUILayoutUtility.GetRect(
                0f,
                labelMax * 1.5f,
                EditorGUIUtility.singleLineHeight,
                EditorGUIUtility.singleLineHeight,
                EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(65f),
                GUILayout.MaxWidth(300f));

            if (!searchTextByBrowser.TryGetValue(id, out var text))
                text = string.Empty;

            EditorGUI.BeginChangeCheck();
            var newText = EditorGUI.TextField(rect, text, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
                ApplySearchQuery(projectBrowser, newText);
        }

        static void DrawOpenInSearchButton()
        {
            var ic = EditorGUIUtility.TrIconContent("SearchJump Icon");
            var c = new GUIContent
            {
                image = ic.image as Texture,
                text = string.Empty,
                tooltip = BuildOpenInSearchTooltip(),
            };
            var jumpStyle = ToolbarSearchJumpButtonStyleOrFallback();
            var r = GUILayoutUtility.GetRect(c, jumpStyle, GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight));
            if (GUI.Button(r, c, jumpStyle))
            {
                try
                {
                    SearchService.ShowWindow();
                }
                catch
                {
                    TryExecuteMenuFallbackSearch();
                }
            }

            PushToolbarTooltipHit(r, c.tooltip);
        }

        static void TryExecuteMenuFallbackSearch()
        {
            string[] candidates =
            {
                "Edit/Search/Search All...",
                "Window/General/Search",
                "Window/Search/All",
                "Edit/Search/Quick Search",
            };
            foreach (var path in candidates)
            {
                try
                {
                    EditorApplication.ExecuteMenuItem(path);
                    return;
                }
                catch
                {
                }
            }
        }

        static void DrawSearchByTypeMenu(EditorWindow projectBrowser)
        {
            if (InvokeProjectBrowserToolbarMethod(projectBrowser, typeDropDownMethod))
                return;
        }

        static void DrawSearchByLabelMenu(EditorWindow projectBrowser)
        {
            if (InvokeProjectBrowserToolbarMethod(projectBrowser, assetLabelsDropDownMethod))
                return;
        }

        static void DrawLogTypeMenu(EditorWindow projectBrowser)
        {
            if (logTypeDropDownMethod == null)
                return;
            if (InvokeProjectBrowserToolbarMethod(projectBrowser, logTypeDropDownMethod))
                return;
        }

        static void DrawIndieaAssetTagsMenu()
        {
            var c = new GUIContent
            {
                image = EditorGUIUtility.IconContent("d_SignalAsset Icon")?.image as Texture,
                text = string.Empty,
                tooltip = ProjectBrowserIconTooltips.SearchByAssetTag,
            };
            var sizeRef = CloneIconContent(EditorGUIUtility.IconContent("Preset.Context"));
            var r = GUILayoutUtility.GetRect(sizeRef, EditorStyles.toolbarButton);
            if (CanCaptureDropdownInput())
            {
                if (EditorGUI.DropdownButton(r, c, FocusType.Passive, EditorStyles.toolbarButton))
                    AssetTagsProjectBrowserFilter.Show(r);
            }
            else
            {
                if (Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 &&
                    r.Contains(Event.current.mousePosition))
                {
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    GUI.FocusControl(null);
                    Event.current.Use();
                    AssetTagsProjectBrowserFilter.Show(r);
                }
                GUI.Label(r, c, EditorStyles.toolbarButton);
            }

            PushToolbarTooltipHit(r, c.tooltip);
        }

        static bool CanCaptureDropdownInput() =>
            GUIUtility.hotControl == 0 && !EditorGUIUtility.editingTextField;

        static void DrawSaveSearch(EditorWindow projectBrowser)
        {
            if (InvokeProjectBrowserToolbarMethod(projectBrowser, buttonSaveFilterMethod))
                return;
            DrawSaveSearchFallback(projectBrowser);
        }

        static void DrawHiddenPackagesToggle(EditorWindow projectBrowser)
        {
            if (InvokeProjectBrowserToolbarMethod(projectBrowser, toggleHiddenPackagesMethod))
                return;
            DrawHiddenPackagesToggleFallback(projectBrowser);
        }

        static void DrawSaveSearchFallback(EditorWindow projectBrowser)
        {
            if (buttonSaveFilterMethod == null)
                return;
            try
            {
                buttonSaveFilterMethod.Invoke(projectBrowser, null);
            }
            catch
            {
            }
        }

        static void DrawHiddenPackagesToggleFallback(EditorWindow projectBrowser)
        {
            if (toggleHiddenPackagesMethod == null)
                return;
            try
            {
                toggleHiddenPackagesMethod.Invoke(projectBrowser, null);
            }
            catch
            {
            }
        }

        static float GetDirectoriesAreaWidth(EditorWindow projectBrowser)
        {
            if (directoriesAreaWidthField == null)
                return 0f;
            try
            {
                return (float)directoriesAreaWidthField.GetValue(projectBrowser);
            }
            catch
            {
                return 0f;
            }
        }

        static bool IsTwoColumns(EditorWindow w)
        {
            if (isTwoColumnsMethod == null || w == null)
                return true;
            try
            {
                return (bool)isTwoColumnsMethod.Invoke(w, null);
            }
            catch
            {
                return true;
            }
        }
    }
}
#endif
