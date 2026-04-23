#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public class AssetTagsManager
    {
        static readonly StringComparer TagComparer = StringComparer.OrdinalIgnoreCase;
        static readonly HashSet<string> warnedAssetPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public const string RootFolderPath = "Assets/INDiEA/Asset Tags";

        public const string DataAssetPath = RootFolderPath + "/AssetTagsData.asset";
        public const string SettingsAssetPath = RootFolderPath + "/AssetTagsSettings.asset";

        static AssetTagsManager instance;
        AssetTagsData data;
        AssetTagsList list;
        string workstationToken;

        public delegate void TagsChangedHandler();

        public static event TagsChangedHandler OnTagsChanged;

        static AssetTagsManager()
        {
            try
            {
                EnsureCoreAssetsExist();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[AssetTags] Failed to ensure core assets during static initialization. " +
                    $"The system will retry lazily on next access.\n{exception}");
            }
        }

        public static AssetTagsManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new AssetTagsManager();
                    instance.EnsureLoaded();
                }

                return instance;
            }
        }

        public static void EnsureCoreAssetsExist()
        {
            AssetTagsJsonRepository.EnsureInfrastructure();
            EnsureRootFolderExists();
            EnsureAssetAtPath(SettingsAssetPath, () => ScriptableObject.CreateInstance<AssetTagsSettings>());
        }

        static void EnsureRootFolderExists()
        {
            if (AssetDatabase.IsValidFolder(RootFolderPath))
                return;

            const string indieaFolder = "Assets/INDiEA";
            if (!AssetDatabase.IsValidFolder(indieaFolder))
                AssetDatabase.CreateFolder("Assets", "INDiEA");
            AssetDatabase.CreateFolder(indieaFolder, "Asset Tags");
        }

        static void EnsureAssetAtPath<T>(string assetPath, Func<T> create) where T : ScriptableObject
        {
            EnsureFolderForAssetPath(assetPath);

            if (AssetDatabase.LoadAssetAtPath<T>(assetPath) != null)
                return;

            // In some Unity versions, static constructors can run before import is fully finalized.
            // Force import once and re-check to avoid false "non-Unity file" warnings.
            TryForceImport(assetPath);
            if (AssetDatabase.LoadAssetAtPath<T>(assetPath) != null)
                return;

            var existingAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (existingAsset != null)
            {
                WarnOnce(
                    assetPath,
                    $"[AssetTags] Expected `{typeof(T).Name}` at `{assetPath}`, " +
                    $"but found `{existingAsset.GetType().Name}`. " +
                    "Skipping auto-creation to avoid overwrite.");
                return;
            }

            if (File.Exists(assetPath))
            {
                WarnOnce(
                    assetPath,
                    $"[AssetTags] A file already exists at `{assetPath}`, but Unity could not load `{typeof(T).Name}` yet. " +
                    "This can happen during early editor initialization/import timing. Auto-creation is skipped to avoid overwrite.");
                return;
            }

            var asset = create();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
        }

        static void TryForceImport(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;
            try
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            catch
            {
            }
        }

        static void WarnOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            if (!string.IsNullOrEmpty(key) && !warnedAssetPathSet.Add(key))
                return;
            Debug.LogWarning(message);
        }

        static void EnsureFolderForAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            var folderPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets")
                return;

            var segments = folderPath.Split('/');
            if (segments.Length == 0 || !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
                return;

            var current = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        void EnsureLoaded()
        {
            EnsureCoreAssetsExist();
            var tokenNow = AssetTagsJsonRepository.WorkstationTokenCached
                ?? AssetTagsClientId.GetOrCreateClientId();

            if (data != null && list != null
                && !string.IsNullOrEmpty(workstationToken)
                && string.Equals(workstationToken, tokenNow, StringComparison.OrdinalIgnoreCase))
                return;

            if (!string.IsNullOrEmpty(workstationToken)
                && !string.Equals(workstationToken, tokenNow, StringComparison.OrdinalIgnoreCase))
            {
                data = null;
                list = null;
            }

            workstationToken = tokenNow;

            if (data != null && list != null)
                return;

            AssetTagsJsonRepository.RebuildMergedFromAllLocalFilesAndGlobalCache(out data, out list);

            NotifyChanged();
        }

        public static void InvalidateLoadedState()
        {
            if (instance == null)
                return;
            instance.data = null;
            instance.list = null;
        }

        public static IEnumerable<AssetTagsData.TagInfo> EnumerateLocalTagRowsForSearch()
        {
            Instance.EnsureLoaded();
            return Instance.data.assetTags;
        }

        public void SyncCurrentSnapshotToLocal()
        {
            EnsureLoaded();
            CommitChanges(saveData: true, saveList: true);
        }

        void NotifyChanged() =>
            OnTagsChanged?.Invoke();

        void CommitChanges(bool saveData, bool saveList)
        {
            EnsureLoaded();
            var dataPath = AssetTagsJsonRepository.GetLocalAssetTagsJsonFullPath(workstationToken);
            var listPath = AssetTagsJsonRepository.GetLocalAssetTagListJsonFullPath(workstationToken);

            if (saveData)
                AssetTagsJsonRepository.SaveDataState(dataPath, data);

            if (saveList)
                AssetTagsJsonRepository.SaveListState(listPath, list);

            AssetTagsJsonRepository.RebuildMergedFromAllLocalFilesAndGlobalCache(out data, out list);

            NotifyChanged();
            EditorApplication.RepaintProjectWindow();
        }

        static bool TryNormalizeTag(string raw, out string normalized)
        {
            normalized = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
            return normalized.Length > 0;
        }

        public void AddTag(string guid, string tag)
        {
            if (data == null || list == null)
                EnsureLoaded();
            if (string.IsNullOrEmpty(guid))
                return;
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;

            data.AddTag(guid, normalizedTag);
            list.AddTag(normalizedTag);
            CommitChanges(saveData: true, saveList: true);
        }

        public void RemoveTag(string guid, string tag)
        {
            if (data == null || list == null)
                EnsureLoaded();
            if (string.IsNullOrEmpty(guid))
                return;
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;

            data.RemoveTag(guid, normalizedTag);
            CommitChanges(saveData: true, saveList: false);
        }

        public List<string> GetTags(string guid)
        {
            if (data == null)
                EnsureLoaded();
            return data.GetTags(guid);
        }

        public List<string> GetAllAvailableTags()
        {
            if (list == null)
                EnsureLoaded();
            return list.GetAvailableTags();
        }

        public void DeleteTag(string tag)
        {
            if (data == null || list == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;

            foreach (var row in data.assetTags.ToArray())
                data.RemoveTag(row.guid, normalizedTag);

            list.RemoveTag(normalizedTag);
            CommitChanges(saveData: true, saveList: true);
        }

        public void RenameTag(string oldTag, string newTag)
        {
            if (data == null || list == null)
                EnsureLoaded();
            if (!TryNormalizeTag(oldTag, out var normalizedOldTag) ||
                !TryNormalizeTag(newTag, out var normalizedNewTag))
                return;
            if (TagComparer.Equals(normalizedOldTag, normalizedNewTag))
                return;
            if (list.GetAvailableTags().Contains(normalizedNewTag, TagComparer))
                return;

            data.RenameTag(normalizedOldTag, normalizedNewTag);
            list.RenameTag(normalizedOldTag, normalizedNewTag);
            CommitChanges(saveData: true, saveList: true);
        }

        void ReorderDataByList()
        {
            data.ReorderAllTagsByGlobalOrder(list.GetAvailableTags());
        }

        void SaveListAndData()
        {
            CommitChanges(saveData: true, saveList: true);
        }

        public void MoveTagUp(string tag)
        {
            if (data == null || list == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;
            list.MoveTagUp(normalizedTag);
            ReorderDataByList();
            SaveListAndData();
        }

        public void MoveTagDown(string tag)
        {
            if (data == null || list == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;
            list.MoveTagDown(normalizedTag);
            ReorderDataByList();
            SaveListAndData();
        }

        public Color GetTagColor(string tag)
        {
            if (list == null)
                EnsureLoaded();
            return TryNormalizeTag(tag, out var normalizedTag)
                ? list.GetTagColor(normalizedTag)
                : new Color(0.19f, 0.38f, 0.77f, 1f);
        }

        public void SetTagColor(string tag, Color color)
        {
            if (list == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;
            list.SetTagColor(normalizedTag, color);
            CommitChanges(saveData: false, saveList: true);
        }

        public void ConvertTagsToAssetLabels()
        {
            if (data == null || list == null)
                EnsureLoaded();

            if (data?.assetTags == null || data.assetTags.Count == 0)
            {
                Debug.Log("[AssetTags] Asset Tags to Asset Labels conversion was skipped because there is no Asset Tags data.");
                return;
            }

            var updated = 0;
            var updatedAssets = new List<string>();
            foreach (var row in data.assetTags)
            {
                if (row == null || string.IsNullOrEmpty(row.guid))
                    continue;

                var path = AssetDatabase.GUIDToAssetPath(row.guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                    continue;

                var previousLabels = AssetDatabase.GetLabels(asset) ?? Array.Empty<string>();
                var fromAssetTags = row.GetTagNames()
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(TagComparer)
                    .ToList();

                var merged = new HashSet<string>(previousLabels, TagComparer);
                for (var i = 0; i < fromAssetTags.Count; i++)
                    merged.Add(fromAssetTags[i]);

                var finalOrdered = merged.ToList();
                finalOrdered.Sort(StringComparer.Ordinal);

                AssetDatabase.SetLabels(asset, finalOrdered.ToArray());
                updated++;
                updatedAssets.Add(path);
            }

            if (updated == 0)
                Debug.Log("[AssetTags] No tagged assets were found, so no Asset Labels were updated.");
            else
            {
                const int maxLines = 30;
                var shown = updatedAssets.Take(maxLines).ToList();
                var lines = string.Join("\n- ", shown);
                if (updatedAssets.Count > maxLines)
                    lines += $"\n- ... (+{updatedAssets.Count - maxLines} more assets)";

                Debug.Log(
                    $"[AssetTags] All Asset Tags were converted to Asset Labels for {updated} asset(s).\n- {lines}");
            }
        }

        public void ConvertAssetLabelsToTags()
        {
            if (data == null || list == null)
                EnsureLoaded();

            var changedData = false;
            var changedList = false;
            var addedAssetTagLines = new List<string>();

            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                    continue;

                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                    continue;

                var labels = AssetDatabase.GetLabels(asset);
                if (labels == null || labels.Length == 0)
                    continue;

                for (var i = 0; i < labels.Length; i++)
                {
                    if (!TryNormalizeTag(labels[i], out var label))
                        continue;

                    var beforeCount = data.GetTags(guid).Count;
                    data.AddTag(guid, label);
                    var addedOnAsset = data.GetTags(guid).Count != beforeCount;
                    if (addedOnAsset)
                    {
                        changedData = true;
                        addedAssetTagLines.Add($"{path} -> {label}");
                    }

                    var beforeTags = list.GetAvailableTags().Count;
                    list.AddTag(label);
                    var addedToList = list.GetAvailableTags().Count != beforeTags;
                    if (addedToList)
                        changedList = true;
                }
            }

            if (!changedData && !changedList)
            {
                Debug.Log("[AssetTags] No new Asset Labels were found, so Asset Tags remained unchanged.");
            }
            else
            {
                const int maxLines = 30;
                var shown = addedAssetTagLines.Take(maxLines).ToList();
                var lines = shown.Count > 0 ? string.Join("\n- ", shown) : "(no new per-asset tags)";
                if (addedAssetTagLines.Count > maxLines)
                    lines += $"\n- ... (+{addedAssetTagLines.Count - maxLines} more entries)";

                Debug.Log(
                    "[AssetTags] All Asset Labels were converted to Asset Tags.\n- " + lines);
            }

            if (changedData || changedList)
                CommitChanges(saveData: changedData, saveList: changedList);
            else
                NotifyChanged();
        }
    }
}
#endif
