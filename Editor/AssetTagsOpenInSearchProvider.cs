#if UNITY_2021_2_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace INDiEA.AssetTags
{
    static class AssetTagsOpenInSearchProvider
    {
        public const string TagQueryPrefix = "tag:";
        public const string TagQueryPrefixLegacy = @"AssetTags\";
        public const string NeedleListAllTaggedAssets = "all";

        static readonly string[] TagPrefixes = { TagQueryPrefixLegacy, TagQueryPrefix };

        [SearchItemProvider]
        internal static SearchProvider CreateMain() =>
            Build("inda_asset_tags", "Asset Tags", TagQueryPrefix);

        [SearchItemProvider]
        internal static SearchProvider CreateLegacy() =>
            Build("inda_asset_tags_legacy", @"Asset Tags (\)", TagQueryPrefixLegacy);

        static SearchProvider Build(string id, string displayName, string filterId)
        {
            return new SearchProvider(id, displayName)
            {
                filterId = filterId,
                isExplicitProvider = true,
                priority = 24,
                fetchItems = (context, items, provider) => Fetch(context, provider),
                fetchThumbnail = (item, context) =>
                    string.IsNullOrEmpty(item.id) ? null : AssetDatabase.GetCachedIcon(item.id) as Texture2D,
                fetchLabel = (item, context) =>
                    !string.IsNullOrEmpty(item.label) ? item.label : FileName(item.id),
                fetchDescription = (item, context) => item.id,
                toObject = (item, type) => AssetDatabase.LoadMainAssetAtPath(item.id),
                trackSelection = (item, context) =>
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(item.id);
                    if (asset != null)
                        EditorGUIUtility.PingObject(asset);
                },
                startDrag = (item, context) =>
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(item.id);
                    if (asset == null)
                        return;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new[] { asset };
                    DragAndDrop.StartDrag(item.label ?? item.id);
                },
            };
        }

        internal static bool TryExtractTagNeedleForProjectSearch(string query, out string needle, out bool listAllTagged)
        {
            needle = null;
            listAllTagged = false;
            if (string.IsNullOrEmpty(query))
                return false;

            var trimmed = query.Trim();
            if (NeedleAfterPrefix(trimmed, out needle))
            {
                listAllTagged = IsAllNeedle(needle);
                return true;
            }

            foreach (var token in trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!NeedleAfterPrefix(token, out needle))
                    continue;
                listAllTagged = IsAllNeedle(needle);
                return true;
            }

            if (!NeedleAfterEmbedded(trimmed, out needle))
                return false;

            listAllTagged = IsAllNeedle(needle);
            return true;
        }

        internal static bool TryStripLeadingTagPrefix(string segment, out string needle)
        {
            needle = null;
            if (string.IsNullOrEmpty(segment))
                return false;

            segment = segment.Trim();
            foreach (var prefix in TagPrefixes)
            {
                if (!segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                needle = segment.Substring(prefix.Length).Trim();
                return true;
            }

            return false;
        }

        internal static IEnumerable<string> EnumerateMatchingAssetPaths(string needle, bool listAllTagged = false)
        {
            if (string.IsNullOrEmpty(needle))
                yield break;

            var listAll = listAllTagged
                && needle.Equals(NeedleListAllTaggedAssets, StringComparison.OrdinalIgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AssetTagsManager.EnsureCoreAssetsExist();
            foreach (var row in AssetTagsManager.EnumerateLocalTagRowsForSearch())
            {
                var tagNames = AssetTagsManager.GetSearchScratchResolvedTags(row);
                if (string.IsNullOrEmpty(row.guid) || tagNames.Count == 0)
                    continue;
                if (!RowMatches(tagNames, needle, listAll))
                    continue;

                var path = AssetDatabase.GUIDToAssetPath(row.guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;
                path = path.Replace('\\', '/');
                if (!seen.Add(path))
                    continue;

                yield return path;
            }
        }

        internal static bool TagMatches(string tag, string needle)
        {
            if (tag.Equals(needle, StringComparison.OrdinalIgnoreCase))
                return true;
            return tag.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool NeedleAfterPrefix(string segment, out string needle) =>
            TryStripLeadingTagPrefix(segment, out needle);

        static bool IsAllNeedle(string needle) =>
            needle.Equals(NeedleListAllTaggedAssets, StringComparison.OrdinalIgnoreCase);

        static bool NeedleAfterEmbedded(string fullQuery, out string needle)
        {
            needle = null;
            var bestStart = int.MaxValue;
            string bestPrefix = null;
            foreach (var prefix in TagPrefixes)
            {
                var start = fullQuery.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    continue;
                if (start < bestStart ||
                    (start == bestStart && bestPrefix != null && prefix.Length > bestPrefix.Length))
                {
                    bestStart = start;
                    bestPrefix = prefix;
                }
            }

            if (bestPrefix == null)
                return false;

            var tail = fullQuery.Substring(bestStart + bestPrefix.Length).Trim();
            if (tail.Length == 0)
            {
                needle = string.Empty;
                return true;
            }

            var space = tail.IndexOfAny(new[] { ' ', '\t' });
            needle = (space < 0 ? tail : tail.Substring(0, space)).Trim();
            return true;
        }

        static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            var normalized = path.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }

        static bool IsOurFilter(string filterId) =>
            !string.IsNullOrEmpty(filterId) &&
            (string.Equals(filterId, TagQueryPrefix, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(filterId, TagQueryPrefixLegacy, StringComparison.OrdinalIgnoreCase));

        static IEnumerator Fetch(SearchContext context, SearchProvider provider)
        {
            var needle = context.searchPhrase?.Trim() ?? string.Empty;
            if (needle.Length == 0)
                yield break;

            var listAll = IsOurFilter(provider.filterId)
                && needle.Equals(NeedleListAllTaggedAssets, StringComparison.OrdinalIgnoreCase);

            foreach (var path in EnumerateMatchingAssetPaths(needle, listAll))
            {
                var label = FileName(path);
                yield return provider.CreateItem(context, path, label, path, null, null);
            }
        }

        static bool RowMatches(IReadOnlyList<string> tags, string needle, bool listAll)
        {
            if (tags == null || tags.Count == 0)
                return false;

            if (listAll)
            {
                for (var i = 0; i < tags.Count; i++)
                {
                    if (!string.IsNullOrEmpty(tags[i]))
                        return true;
                }

                return false;
            }

            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (string.IsNullOrEmpty(tag))
                    continue;
                if (TagMatches(tag, needle))
                    return true;
            }

            return false;
        }
    }

    public static class AssetTagsOpenInSearchIndexer
    {
        public const string PropertyName = "assettags";
        const int Version = 2;

        static Dictionary<string, string[]> tagsByGuid;

        public static void InvalidateTagMap() =>
            tagsByGuid = null;

        static void EnsureMap()
        {
            if (tagsByGuid != null)
                return;

            tagsByGuid = new Dictionary<string, string[]>(StringComparer.Ordinal);
            AssetTagsManager.EnsureCoreAssetsExist();
            foreach (var row in AssetTagsManager.EnumerateLocalTagRowsForSearch())
            {
                var tagNames = AssetTagsManager.GetSearchScratchResolvedTags(row);
                if (string.IsNullOrEmpty(row.guid) || tagNames.Count == 0)
                    continue;
                tagsByGuid[row.guid] = tagNames.ToArray();
            }
        }

        [CustomObjectIndexer(typeof(UnityEngine.Object), version = Version)]
        static void IndexAssetTags(CustomObjectIndexerTarget context, ObjectIndexer indexer)
        {
            var path = context.id;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return;

            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                return;

            EnsureMap();
            if (tagsByGuid == null
                || !tagsByGuid.TryGetValue(guid, out var tags)
                || tags == null
                || tags.Length == 0)
                return;

            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag))
                    continue;
                indexer.IndexProperty(context.documentIndex, PropertyName, tag, saveKeyword: true);
            }
        }

        internal static Dictionary<string, string> BuildTagSignatureByGuid()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            AssetTagsManager.EnsureCoreAssetsExist();
            foreach (var row in AssetTagsManager.EnumerateLocalTagRowsForSearch())
            {
                var tagNames = AssetTagsManager.GetSearchScratchResolvedTags(row);
                if (string.IsNullOrEmpty(row.guid) || tagNames.Count == 0)
                    continue;
                result[row.guid] = CanonicalizeTags(tagNames);
            }

            return result;
        }

        static string CanonicalizeTags(IReadOnlyList<string> tags)
        {
            if (tags == null || tags.Count == 0)
                return string.Empty;

            var cleaned = new List<string>(tags.Count);
            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                cleaned.Add(tag.Trim());
            }

            if (cleaned.Count == 0)
                return string.Empty;

            cleaned.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("\u001f", cleaned);
        }

        public static void ReindexGuidsForSearch(IEnumerable<string> guids)
        {
            if (guids == null)
                return;

            InvalidateTagMap();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in guids)
            {
                if (string.IsNullOrEmpty(guid))
                    continue;

                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath)
                    && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    && AssetPathExistsForReindex(assetPath)
                    && IsSearchReindexImportSafe(assetPath))
                    paths.Add(assetPath);
            }

            if (paths.Count == 0)
                return;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static bool IsSearchReindexImportSafe(string assetPath)
        {
            var extension = System.IO.Path.GetExtension(assetPath);
            if (string.IsNullOrEmpty(extension))
                return true;

            switch (extension.ToLowerInvariant())
            {
                case ".cs":
                case ".asmdef":
                case ".asmref":
                case ".dll":
                case ".rsp":
                    return false;
                default:
                    return true;
            }
        }

        static bool AssetPathExistsForReindex(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            if (AssetDatabase.IsValidFolder(assetPath))
                return true;
            return AssetDatabase.LoadMainAssetAtPath(assetPath) != null;
        }

        public static void ReindexAllTaggedAssetsForSearch()
        {
            ReindexGuidsForSearch(BuildTagSignatureByGuid().Keys);
        }
    }

    [InitializeOnLoad]
    internal static class AssetTagsSearchReindexCoordinator
    {
        static Dictionary<string, string> lastTagSignatureByGuid;
        static AssetTagsSettings settingsCache;

        static AssetTagsSearchReindexCoordinator()
        {
            lastTagSignatureByGuid = AssetTagsOpenInSearchIndexer.BuildTagSignatureByGuid();
            AssetTagsManager.OnTagsChanged += OnTagsChanged;
        }

        public static void InvalidateSettingsCache()
        {
            settingsCache = null;
        }

        static bool IsIndexingSearchAfterTagChangesEnabled()
        {
            if (settingsCache == null)
            {
                AssetTagsManager.EnsureCoreAssetsExist();
                settingsCache = AssetDatabase.LoadAssetAtPath<AssetTagsSettings>(AssetTagsManager.SettingsAssetPath);
            }

            return settingsCache == null || settingsCache.IndexingSearchAfterTagChanges;
        }

        static void OnTagsChanged()
        {
            var current = AssetTagsOpenInSearchIndexer.BuildTagSignatureByGuid();
            var changedGuids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pair in current)
            {
                if (!lastTagSignatureByGuid.TryGetValue(pair.Key, out var previous) || !string.Equals(previous, pair.Value, StringComparison.Ordinal))
                    changedGuids.Add(pair.Key);
            }

            foreach (var pair in lastTagSignatureByGuid)
            {
                if (!current.ContainsKey(pair.Key))
                    changedGuids.Add(pair.Key);
            }

            if (changedGuids.Count > 0 && IsIndexingSearchAfterTagChangesEnabled())
                AssetTagsOpenInSearchIndexer.ReindexGuidsForSearch(changedGuids);

            lastTagSignatureByGuid = current;
        }

        static int refreshSnapshotGeneration;

        internal static void RefreshSnapshotFromAsset()
        {
            var gen = ++refreshSnapshotGeneration;
            EditorApplication.delayCall += () =>
            {
                if (gen != refreshSnapshotGeneration)
                    return;
                AssetTagsManager.InvalidateLoadedState();
                OnTagsChanged();
            };
        }
    }

    internal sealed class AssetTagsOpenInSearchIndexPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedToAssetPaths,
            string[] movedFromAssetPaths)
        {
            if (importedAssets == null)
                return;

            var localRoot = AssetTagsJsonRepository.LocalDataFolderAssetPath.Replace('\\', '/');
            foreach (var path in importedAssets)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                var normalized = path.Replace('\\', '/');
                var isLocalJson = normalized.StartsWith(localRoot + "/", StringComparison.OrdinalIgnoreCase)
                    && normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                var isLegacySo = string.Equals(normalized, AssetTagsManager.DataAssetPath, StringComparison.OrdinalIgnoreCase);
                if (!isLocalJson && !isLegacySo)
                    continue;

                AssetTagsSearchReindexCoordinator.RefreshSnapshotFromAsset();
                return;
            }
        }
    }
}
#endif
