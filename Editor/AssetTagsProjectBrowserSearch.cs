#if UNITY_2021_2_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    [InitializeOnLoad]
    public static class AssetTagsProjectBrowserSearch
    {
        const string HarmonyId = "com.indiea.assettags.projectsearchsession.search";

        static readonly Type projectBrowserType =
            typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");

        static MethodInfo setSearchMethod;
        static MethodInfo getAllBrowsersMethod;
        static PropertyInfo searchFilterProperty;
        static bool harmonyInstallAttempted;
        static bool warnedSearchPatchFailure;
        static AssetTagsSettings settingsCache;

        internal static bool IsProjectSearchSessionHarmonyPatched { get; private set; }

        static AssetTagsProjectBrowserSearch() =>
            TryInstallSearchHarmony();

        static void TryInstallSearchHarmony()
        {
            if (harmonyInstallAttempted)
                return;
            harmonyInstallAttempted = true;

            try
            {
                var sessionType =
                    typeof(Editor).Assembly.GetType("UnityEditor.SearchService.ProjectSearchSessionHandler");
                if (sessionType == null)
                {
                    LogDebugWarningOnce(
                        ref warnedSearchPatchFailure,
                        "[AssetTags] Failed to patch project search: ProjectSearchSessionHandler type was not found.");
                    return;
                }

                var search = sessionType.GetMethod(
                    "Search",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), typeof(Action<IEnumerable<string>>) },
                    null);
                if (search == null)
                {
                    LogDebugWarningOnce(
                        ref warnedSearchPatchFailure,
                        "[AssetTags] Failed to patch project search: Search method was not found.");
                    return;
                }

                var prefix = typeof(AssetTagsProjectBrowserSearch).GetMethod(
                    nameof(PrefixSwallowAsyncForTagQuery),
                    BindingFlags.Static | BindingFlags.NonPublic);
                var postfix = typeof(AssetTagsProjectBrowserSearch).GetMethod(
                    nameof(PostfixInjectTagPaths),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null || postfix == null)
                {
                    LogDebugWarningOnce(
                        ref warnedSearchPatchFailure,
                        "[AssetTags] Failed to patch project search: prefix/postfix methods were not found.");
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
                        ref warnedSearchPatchFailure,
                        "[AssetTags] Harmony was not found. Project search tag injection is disabled.");
                    return;
                }

                var harmonyCtor = harmonyType.GetConstructor(new[] { typeof(string) });
                var harmonyMethodCtor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
                var patch = harmonyType.GetMethod(
                    "Patch",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(MethodBase), harmonyMethodType, harmonyMethodType, harmonyMethodType, harmonyMethodType },
                    null);
                if (harmonyCtor == null || harmonyMethodCtor == null || patch == null)
                {
                    LogDebugWarningOnce(
                        ref warnedSearchPatchFailure,
                        "[AssetTags] Harmony patch API was not found. Project search tag injection is disabled.");
                    return;
                }

                var instance = harmonyCtor.Invoke(new object[] { HarmonyId });
                var prefixHm = harmonyMethodCtor.Invoke(new object[] { prefix });
                var postfixHm = harmonyMethodCtor.Invoke(new object[] { postfix });
                patch.Invoke(instance, new object[] { search, prefixHm, postfixHm, null, null });
                IsProjectSearchSessionHarmonyPatched = true;
            }
            catch (Exception exception)
            {
                LogDebugWarningOnce(
                    ref warnedSearchPatchFailure,
                    "[AssetTags] Exception while patching project search. Tag injection is disabled.",
                    exception);
            }
        }

        static void EnsureSettingsLoaded()
        {
            if (settingsCache != null)
                return;

            AssetTagsManager.EnsureCoreAssetsExist();
            settingsCache = AssetDatabase.LoadAssetAtPath<AssetTagsSettings>(AssetTagsManager.SettingsAssetPath);
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

        static void PrefixSwallowAsyncForTagQuery(
            object __instance,
            string query,
            ref Action<IEnumerable<string>> asyncItemsReceived)
        {
            if (!AssetTagsOpenInSearchProvider.TryExtractTagNeedleForProjectSearch(query, out _, out _))
                return;
            asyncItemsReceived = _ => { };
        }

        static void PostfixInjectTagPaths(
            object __instance,
            string query,
            Action<IEnumerable<string>> asyncItemsReceived,
            ref IEnumerable<string> __result)
        {
            if (!AssetTagsOpenInSearchProvider.TryExtractTagNeedleForProjectSearch(
                    query ?? string.Empty,
                    out var needle,
                    out var listAllTagged))
                return;
            if (needle == null)
                return;

            __result = needle.Length == 0
                ? Array.Empty<string>()
                : new List<string>(AssetTagsOpenInSearchProvider.EnumerateMatchingAssetPaths(needle, listAllTagged));
        }

        public static void ApplyTagNeedle(string needle)
        {
            var current = ReadProjectSearchText();
            var rest = RemoveTagTokens(current);
            if (string.IsNullOrEmpty(needle))
            {
                ApplySearchToBrowsers(rest);
                return;
            }

            var token = AssetTagsOpenInSearchProvider.TagQueryPrefix + needle;
            var merged = string.IsNullOrEmpty(rest) ? token : $"{rest} {token}";
            ApplySearchToBrowsers(merged);
        }

        public static void ClearSearch() =>
            ApplySearchToBrowsers(string.Empty);

        public static void SetProjectSearch(string text) =>
            ApplySearchToBrowsers(text ?? string.Empty);

        public static string GetAppliedTagNeedle() =>
            LastTagNeedle(ReadProjectSearchText());

        static string[] SplitQuery(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return Array.Empty<string>();
            return search.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        static string RemoveTagTokens(string search)
        {
            var parts = new List<string>();
            foreach (var token in SplitQuery(search))
            {
                if (AssetTagsOpenInSearchProvider.TryStripLeadingTagPrefix(token, out _))
                    continue;
                parts.Add(token);
            }

            return string.Join(" ", parts).Trim();
        }

        static string LastTagNeedle(string search)
        {
            var needle = string.Empty;
            foreach (var token in SplitQuery(search))
            {
                if (AssetTagsOpenInSearchProvider.TryStripLeadingTagPrefix(token, out var n))
                    needle = n ?? string.Empty;
            }

            return needle ?? string.Empty;
        }

        static string ReadProjectSearchText()
        {
            if (projectBrowserType == null)
                return string.Empty;

            searchFilterProperty ??= projectBrowserType.GetProperty(
                "searchFilter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (searchFilterProperty == null)
                return string.Empty;

            getAllBrowsersMethod ??= projectBrowserType.GetMethod(
                "GetAllProjectBrowsers",
                BindingFlags.Public | BindingFlags.Static);

            var browsers = getAllBrowsersMethod?.Invoke(null, null) as IEnumerable;
            if (browsers == null)
                return string.Empty;

            foreach (var browser in browsers)
            {
                if (browser == null)
                    continue;
                try
                {
                    return searchFilterProperty.GetValue(browser, null) as string ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        static void ApplySearchToBrowsers(string text)
        {
            if (projectBrowserType == null)
                return;

            setSearchMethod ??= projectBrowserType.GetMethod(
                "SetSearch",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            if (setSearchMethod == null)
                return;

            getAllBrowsersMethod ??= projectBrowserType.GetMethod(
                "GetAllProjectBrowsers",
                BindingFlags.Public | BindingFlags.Static);

            var browsers = getAllBrowsersMethod?.Invoke(null, null) as IEnumerable;
            if (browsers != null)
            {
                foreach (var browser in browsers)
                {
                    if (browser != null)
                        setSearchMethod.Invoke(browser, new object[] { text });
                }

                EditorWindow.GetWindow(projectBrowserType)?.Focus();
                return;
            }

            var window = EditorWindow.GetWindow(projectBrowserType);
            if (window != null)
            {
                setSearchMethod.Invoke(window, new object[] { text });
                window.Focus();
            }
        }
    }
}
#endif
