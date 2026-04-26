#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public class AssetTagsManager
    {
        static readonly StringComparer TagComparer = StringComparer.OrdinalIgnoreCase;

        const string FallbackRootFolderPath = "Assets/INDiEA/Asset Tags";
        const string ManagerScriptFileSuffix = "/Scripts/AssetTagsManager.cs";

        static string cachedRootFolderPath;

        public static string RootFolderPath
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedRootFolderPath))
                    return cachedRootFolderPath;
                cachedRootFolderPath = ResolveRootFolderPath();
                return cachedRootFolderPath;
            }
        }

        public static string DataAssetPath => $"{RootFolderPath}/AssetTagsData.asset";

        public static string SettingsAssetPath => $"{RootFolderPath}/AssetTagsSettings.asset";

        public static void InvalidateRootFolderPathCache()
        {
            cachedRootFolderPath = null;
        }

        static string ResolveRootFolderPath()
        {
            try
            {
                foreach (var scriptAsset in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    if (scriptAsset == null || scriptAsset.GetClass() != typeof(AssetTagsManager))
                        continue;
                    var scriptPath = AssetDatabase.GetAssetPath(scriptAsset);
                    if (string.IsNullOrEmpty(scriptPath))
                        continue;
                    var normalized = scriptPath.Replace('\\', '/');
                    if (!normalized.EndsWith(ManagerScriptFileSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var root = normalized.Substring(0, normalized.Length - ManagerScriptFileSuffix.Length);
                    if (!string.IsNullOrEmpty(root))
                        return root;
                }
            }
            catch
            {
            }

            return FallbackRootFolderPath;
        }

        public static string AssetPathToFullPathOnDisk(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            var n = assetPath.Replace('\\', '/').TrimEnd('/');
            if (n.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var tail = n.Substring("Assets/".Length);
                return Path.GetFullPath(Path.Combine(Application.dataPath, tail.Replace('/', Path.DirectorySeparatorChar)));
            }

            if (n.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, n.Replace('/', Path.DirectorySeparatorChar)));
            }

            return null;
        }

        public static bool TryDiskFullPathToAssetPath(string fullPath, out string assetPath)
        {
            assetPath = null;
            if (string.IsNullOrEmpty(fullPath))
                return false;
            fullPath = Path.GetFullPath(fullPath);
            var dataRoot = Path.GetFullPath(Application.dataPath);
            if (fullPath.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase))
            {
                var tail = fullPath.Length > dataRoot.Length
                    ? fullPath.Substring(dataRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : string.Empty;
                assetPath = "Assets/" + tail.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                return true;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(dataRoot, ".."));
            var packagesRoot = Path.GetFullPath(Path.Combine(projectRoot, "Packages"));
            if (fullPath.StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase))
            {
                var tail = fullPath.Length > packagesRoot.Length
                    ? fullPath.Substring(packagesRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : string.Empty;
                assetPath = "Packages/" + tail.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                return true;
            }

            return false;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void ClearRootFolderPathOnScriptReload() => InvalidateRootFolderPathCache();

        static AssetTagsManager instance;
        static AssetTagsData searchRowScratch;
        static AssetTagsList searchListScratch;

        AssetTagsData mergedViewData;
        AssetTagsList mergedViewList;

        AssetTagsData localData;
        AssetTagsList localList;
        Dictionary<string, AssetTagsJsonRepository.HiddenTag> hiddenTags =
            new Dictionary<string, AssetTagsJsonRepository.HiddenTag>(StringComparer.OrdinalIgnoreCase);

        string workstationToken;

        public delegate void TagsChangedHandler();

        public static event TagsChangedHandler OnTagsChanged;

        static AssetTagsManager()
        {
            try
            {
                EnsureCoreAssetsExist();
            }
            catch
            {
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
                else if (instance.mergedViewData == null || instance.mergedViewList == null
                    || instance.localData == null || instance.localList == null)
                    instance.EnsureLoaded();

                return instance;
            }
        }

        public static void EnsureCoreAssetsExist()
        {
            AssetTagsJsonRepository.EnsureInfrastructure();
            EnsureRootFolderExists();
            EnsureAssetAtPath(SettingsAssetPath, () => ScriptableObject.CreateInstance<AssetTagsSettings>());
        }

        public static void EnsureRootFolderExists()
        {
            if (AssetTagsJsonRepository.IsRunningOnAssetImportWorker)
                return;

            if (AssetDatabase.IsValidFolder(RootFolderPath))
                return;

            var folder = RootFolderPath.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder))
                return;
            var parts = folder.Split('/');
            if (parts.Length < 2)
                return;
            if (!string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[0], "Packages", StringComparison.OrdinalIgnoreCase))
                return;

            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static void EnsureAssetAtPath<T>(string assetPath, Func<T> create, bool fromDelayedRetry = false) where T : ScriptableObject
        {
            if (AssetTagsJsonRepository.IsRunningOnAssetImportWorker)
            {
                EditorApplication.delayCall += () => EnsureAssetAtPath(assetPath, create, fromDelayedRetry);
                return;
            }

            EnsureFolderForAssetPath(assetPath);

            if (AssetDatabase.LoadAssetAtPath<T>(assetPath) != null)
                return;

            TryForceImport(assetPath);
            if (AssetDatabase.LoadAssetAtPath<T>(assetPath) != null)
                return;

            var existingAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (existingAsset != null)
                return;

            if (File.Exists(assetPath))
            {
                if (!fromDelayedRetry)
                {
                    EditorApplication.delayCall += () => EnsureAssetAtPath(assetPath, create, fromDelayedRetry: true);
                    return;
                }

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
            if (AssetTagsJsonRepository.IsRunningOnAssetImportWorker)
                return;
            try
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            catch
            {
            }
        }

        static void EnsureFolderForAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            var folderPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets")
                return;

            var segments = folderPath.Split('/');
            if (segments.Length == 0
                || (!string.Equals(segments[0], "Assets", StringComparison.Ordinal)
                    && !string.Equals(segments[0], "Packages", StringComparison.Ordinal)))
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

            if (mergedViewData != null && mergedViewList != null && localData != null && localList != null
                && !string.IsNullOrEmpty(workstationToken)
                && string.Equals(workstationToken, tokenNow, StringComparison.OrdinalIgnoreCase))
                return;

            if (!string.IsNullOrEmpty(workstationToken)
                && !string.Equals(workstationToken, tokenNow, StringComparison.OrdinalIgnoreCase))
            {
                mergedViewData = null;
                mergedViewList = null;
                localData = null;
                localList = null;
                hiddenTags = new Dictionary<string, AssetTagsJsonRepository.HiddenTag>(StringComparer.OrdinalIgnoreCase);
            }

            workstationToken = tokenNow;

            if (mergedViewData != null && mergedViewList != null && localData != null && localList != null)
                return;

            if (localData == null)
                localData = new AssetTagsData();
            if (localList == null)
                localList = new AssetTagsList();

            AssetTagsJsonRepository.RebuildMergedFromAllLocalFilesAndGlobalCache(out mergedViewData, out mergedViewList);
            AssetTagsJsonRepository.LoadLocal(workstationToken, localData, localList);
            hiddenTags = AssetTagsJsonRepository.LoadHiddenTags(workstationToken);

            NotifyChanged();
        }

        public static void InvalidateLoadedState()
        {
            if (instance == null)
                return;
            instance.mergedViewData = null;
            instance.mergedViewList = null;
            instance.localData = null;
            instance.localList = null;
            searchRowScratch = null;
            searchListScratch = null;
        }

        static void EnsureSearchScratchLoaded()
        {
            if (searchRowScratch == null)
                searchRowScratch = new AssetTagsData();
            if (searchListScratch == null)
                searchListScratch = new AssetTagsList();
            AssetTagsJsonRepository.LoadGlobalDataCacheInto(searchRowScratch, searchListScratch);
        }

        public static IEnumerable<AssetTagsData.TagInfo> EnumerateLocalTagRowsForSearch()
        {
            EnsureSearchScratchLoaded();
            if (searchRowScratch.assetTags == null || searchRowScratch.assetTags.Count == 0)
                return Array.Empty<AssetTagsData.TagInfo>();
            return new List<AssetTagsData.TagInfo>(searchRowScratch.assetTags);
        }

        public static IReadOnlyList<string> GetSearchScratchResolvedTags(AssetTagsData.TagInfo row)
        {
            EnsureSearchScratchLoaded();
            if (row == null)
                return Array.Empty<string>();
            return row.GetResolvedTagNames(searchListScratch);
        }

        public void SaveCurrentTagsToLocalData()
        {
            EnsureLoaded();
            CommitChanges(saveData: true, saveList: true);
        }

        public void ClearCurrentLocalData()
        {
            EnsureLoaded();
            AddHiddenIdsFromTagList(mergedViewList);
            AddHiddenIdsFromTagList(localList);
            localData.assetTags.Clear();
            localList.tags.Clear();
            SaveHiddenTags();
            CommitChanges(saveData: true, saveList: true);
        }

        public static bool IsDeletedTagRecordMergeEnabled()
        {
            EnsureCoreAssetsExist();
            var settings = AssetDatabase.LoadAssetAtPath<AssetTagsSettings>(SettingsAssetPath);
            return settings == null || settings.MergeDeletedTagRecords;
        }

        void AddHiddenIdsFromTagList(AssetTagsList tagList)
        {
            if (tagList?.tags == null)
                return;

            for (var i = 0; i < tagList.tags.Count; i++)
            {
                var row = tagList.tags[i];
                if (row == null || !AssetTagsTagId.IsWellFormed(row.tagId))
                    continue;
                AddHiddenTag(row.tagId.Trim());
            }
        }

        void NotifyChanged() =>
            OnTagsChanged?.Invoke();

        void CommitChanges(bool saveData, bool saveList)
        {
            EnsureLoaded();
            var dataPath = AssetTagsJsonRepository.GetLocalAssetTagsJsonFullPath(workstationToken);
            var listPath = AssetTagsJsonRepository.GetLocalAssetTagListJsonFullPath(workstationToken);

            if (saveData)
                AssetTagsJsonRepository.SaveDataState(dataPath, localData);

            var listDirty = false;
            if (saveData)
                listDirty = ReconcileTagListWithData();

            var saveListFile = saveList || listDirty;
            if (saveListFile)
                AssetTagsJsonRepository.SaveListState(listPath, localList);

            AssetTagsJsonRepository.RebuildMergedFromAllLocalFilesAndGlobalCache(out mergedViewData, out mergedViewList);
            AssetTagsJsonRepository.LoadLocal(workstationToken, localData, localList);

            NotifyChanged();
            EditorApplication.RepaintProjectWindow();
        }

        bool ReconcileTagListWithData()
        {
            if (localData?.assetTags == null || localList == null)
                return false;

            var inUseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in localData.assetTags)
            {
                if (row?.tags == null)
                    continue;
                for (var i = 0; i < row.tags.Count; i++)
                {
                    var e = row.tags[i];
                    if (e == null || !AssetTagsTagId.IsWellFormed(e.tagId))
                        continue;
                    inUseIds.Add(e.tagId.Trim());
                }
            }

            var changed = false;
            foreach (var tagId in inUseIds)
            {
                var had = localList.TryFind(tagId, out _);
                EnsureLocalRowById(tagId);
                if (!had && localList.TryFind(tagId, out _))
                    changed = true;
            }

            return changed;
        }

        bool MergedHasTag(string normalizedTag) =>
            !string.IsNullOrEmpty(normalizedTag) && mergedViewList != null && mergedViewList.Contains(normalizedTag);

        void EnsureLocalRowForMutation(string normalizedTag)
        {
            if (string.IsNullOrEmpty(normalizedTag) || localList == null)
                return;
            if (localList.Contains(normalizedTag))
                return;
            if (mergedViewList == null || !mergedViewList.Contains(normalizedTag))
            {
                if (localList.TryAddTagIfMissing(normalizedTag))
                    RestampTagToMergedTail(normalizedTag);
                return;
            }

            foreach (var src in mergedViewList.tags)
            {
                if (src == null || string.IsNullOrWhiteSpace(src.tagName))
                    continue;
                if (!TagComparer.Equals(src.tagName.Trim(), normalizedTag))
                    continue;
                var copy = new AssetTagsList.TagInfo(normalizedTag, src.color)
                {
                    tagId = AssetTagsTagId.IsWellFormed(src.tagId) ? src.tagId.Trim() : null,
                    tagUpdatedAt = src.tagUpdatedAt,
                    tagUpdatedBy = src.tagUpdatedBy,
                    orderKey = string.IsNullOrWhiteSpace(src.orderKey) ? null : src.orderKey.Trim(),
                    orderUpdatedAt = src.orderUpdatedAt,
                    orderUpdatedBy = src.orderUpdatedBy,
                    order = src.order,
                };
                copy.StampTag();
                localList.ReplaceOrAddListEntry(copy);
                return;
            }

            if (localList.TryAddTagIfMissing(normalizedTag))
                RestampTagToMergedTail(normalizedTag);
        }

        bool EnsureLocalRowForOrder(string tag)
        {
            if (string.IsNullOrEmpty(tag) || localList == null)
                return false;
            if (localList.Contains(tag))
                return true;

            AssetTagsList.TagInfo src = null;
            if (mergedViewList?.tags != null)
            {
                src = mergedViewList.tags.Find(x =>
                    x != null
                    && !string.IsNullOrWhiteSpace(x.tagName)
                    && TagComparer.Equals(x.tagName.Trim(), tag));
            }

            if (src != null)
            {
                var copy = new AssetTagsList.TagInfo(tag, src.color)
                {
                    tagId = AssetTagsTagId.IsWellFormed(src.tagId) ? src.tagId.Trim() : null,
                    tagUpdatedAt = src.tagUpdatedAt,
                    tagUpdatedBy = src.tagUpdatedBy,
                    orderKey = string.IsNullOrWhiteSpace(src.orderKey) ? null : src.orderKey.Trim(),
                    orderUpdatedAt = src.orderUpdatedAt,
                    orderUpdatedBy = src.orderUpdatedBy,
                    order = src.order,
                };
                localList.ReplaceOrAddListEntry(copy);
                return true;
            }

            return localList.TryAddTagIfMissing(tag);
        }

        bool TryGetOrderKey(string tag, out string orderKey)
        {
            orderKey = null;
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            return TryGetOrderKeyFromList(localList, tag, out orderKey)
                   || TryGetOrderKeyFromList(mergedViewList, tag, out orderKey);
        }

        bool TryGetOrderKeyFromList(AssetTagsList list, string tag, out string orderKey)
        {
            orderKey = null;
            if (list?.tags == null || string.IsNullOrWhiteSpace(tag))
                return false;

            for (var i = 0; i < list.tags.Count; i++)
            {
                var row = list.tags[i];
                if (row == null || string.IsNullOrWhiteSpace(row.tagName))
                    continue;
                if (!TagComparer.Equals(row.tagName.Trim(), tag))
                    continue;
                if (!TagSortOrder.TryParseOrderKey(row.orderKey, out _))
                    continue;
                orderKey = row.orderKey.Trim();
                return true;
            }

            return false;
        }

        void RestampTagToMergedTail(string normalizedTag)
        {
            if (string.IsNullOrWhiteSpace(normalizedTag) || localList == null)
                return;
            var row = localList.tags.Find(x =>
                x != null
                && !string.IsNullOrWhiteSpace(x.tagName)
                && TagComparer.Equals(x.tagName.Trim(), normalizedTag));
            if (row == null)
                return;

            ulong max = 0;
            var any = false;

            void Scan(AssetTagsList list, AssetTagsList.TagInfo skip = null)
            {
                if (list?.tags == null)
                    return;
                for (var i = 0; i < list.tags.Count; i++)
                {
                    var t = list.tags[i];
                    if (t == null || string.IsNullOrWhiteSpace(t.tagName) || ReferenceEquals(t, skip))
                        continue;
                    if (!TagSortOrder.TryParseOrderKey(t.orderKey, out var key))
                        continue;
                    any = true;
                    if (key > max)
                        max = key;
                }
            }

            Scan(mergedViewList);
            Scan(localList, row);

            var next = any ? max + TagSortOrder.KeyStep : 0x1000UL;
            row.orderKey = TagSortOrder.FormatOrderKey(next);
            row.order = Math.Max(0, localList.GetAvailableTags().Count - 1);
            row.StampOrderUpdate();
        }

        void EnsureLocalRowById(string tagIdRaw)
        {
            if (!AssetTagsTagId.IsWellFormed(tagIdRaw) || localList == null)
                return;
            var tagId = tagIdRaw.Trim();
            if (localList.TryFind(tagId, out _))
                return;
            if (mergedViewList == null || !mergedViewList.TryFind(tagId, out var src))
                return;

            var copy = new AssetTagsList.TagInfo(src.tagName.Trim(), src.color)
            {
                tagId = tagId,
                tagUpdatedAt = src.tagUpdatedAt,
                tagUpdatedBy = src.tagUpdatedBy,
                orderKey = string.IsNullOrWhiteSpace(src.orderKey) ? null : src.orderKey.Trim(),
                orderUpdatedAt = src.orderUpdatedAt,
                orderUpdatedBy = src.orderUpdatedBy,
                order = src.order,
            };
            copy.StampTag();
            localList.ReplaceOrAddListEntry(copy);
        }

        void SyncMissingFromMerged()
        {
            if (localList == null || mergedViewList == null)
                return;

            for (var i = 0; i < mergedViewList.tags.Count; i++)
            {
                var src = mergedViewList.tags[i];
                if (src == null || string.IsNullOrWhiteSpace(src.tagName))
                    continue;

                if (AssetTagsTagId.IsWellFormed(src.tagId))
                {
                    EnsureLocalRowById(src.tagId.Trim());
                    continue;
                }

                if (!TryNormalizeTag(src.tagName, out var normalizedTag))
                    continue;
                if (!localList.Contains(normalizedTag))
                    localList.TryAddTagIfMissing(normalizedTag);
            }
        }

        static bool TryNormalizeTag(string raw, out string normalized)
        {
            normalized = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
            return normalized.Length > 0;
        }

        bool TryGetTagId(string normalizedTag, out string tagId, bool ensureLocalById)
        {
            tagId = null;
            if (string.IsNullOrWhiteSpace(normalizedTag))
                return false;
            if (localList != null && localList.TryGetId(normalizedTag, out tagId))
                return true;
            if (mergedViewList == null || !mergedViewList.TryGetId(normalizedTag, out tagId))
                return false;
            if (ensureLocalById)
                EnsureLocalRowById(tagId);
            return true;
        }

        bool IsLocallyHiddenById(string tagId) =>
            AssetTagsTagId.IsWellFormed(tagId)
            && hiddenTags != null
            && hiddenTags.ContainsKey(tagId.Trim());

        bool IsLocallyHiddenByTagName(string tagName)
        {
            if (!TryNormalizeTag(tagName, out var normalizedTag))
                return false;
            if (!TryGetTagId(normalizedTag, out var tagId, ensureLocalById: false))
                return false;
            return IsLocallyHiddenById(tagId);
        }

        void AddHiddenTag(string tagId)
        {
            if (!AssetTagsTagId.IsWellFormed(tagId))
                return;
            var id = tagId.Trim();
            hiddenTags[id] = new AssetTagsJsonRepository.HiddenTag(
                id,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                AssetTagsJsonRepository.ResolveLastModifiedBy());
        }

        void SaveHiddenTags() =>
            AssetTagsJsonRepository.SaveHiddenTags(workstationToken, hiddenTags);

        public void AddTag(string guid, string tag)
        {
            if (localData == null || localList == null)
                EnsureLoaded();
            if (string.IsNullOrEmpty(guid))
                return;
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;

            EnsureLocalRowForMutation(normalizedTag);
            if (TryGetTagId(normalizedTag, out var idToUnhide, ensureLocalById: false)
                && hiddenTags.Remove(idToUnhide.Trim()))
                SaveHiddenTags();
            localData.AddTag(guid, normalizedTag, localList);
            CommitChanges(saveData: true, saveList: true);
        }

        public void AddTag(IReadOnlyList<string> guids, string tag)
        {
            if (guids == null || guids.Count == 0)
                return;
            if (localData == null || localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;

            EnsureLocalRowForMutation(normalizedTag);
            if (!localList.TryGetId(normalizedTag, out var tagId))
                return;
            if (hiddenTags.Remove(tagId.Trim()))
                SaveHiddenTags();

            var anyValidGuid = false;
            for (var i = 0; i < guids.Count; i++)
            {
                var guid = string.IsNullOrWhiteSpace(guids[i]) ? string.Empty : guids[i].Trim();
                if (string.IsNullOrEmpty(guid))
                    continue;
                anyValidGuid = true;
                localData.AddTagId(guid, tagId);
            }

            if (!anyValidGuid)
                return;

            CommitChanges(saveData: true, saveList: true);
        }

        public void RemoveTag(string guid, string tag)
        {
            if (localData == null || localList == null)
                EnsureLoaded();
            if (string.IsNullOrEmpty(guid))
                return;
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;

            if (TryGetTagId(normalizedTag, out var tagId, ensureLocalById: false))
                localData.RemoveTagId(guid, tagId);
            else
                localData.RemoveTag(guid, normalizedTag, localList);
            CommitChanges(saveData: true, saveList: false);
        }

        public void RemoveTag(IReadOnlyList<string> guids, string tag)
        {
            if (guids == null || guids.Count == 0)
                return;
            if (localData == null || localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;
            if (!TryGetTagId(normalizedTag, out var tagId, ensureLocalById: false))
                return;
            if (hiddenTags.Remove(tagId.Trim()))
                SaveHiddenTags();

            var anyValidGuid = false;
            for (var i = 0; i < guids.Count; i++)
            {
                var guid = string.IsNullOrWhiteSpace(guids[i]) ? string.Empty : guids[i].Trim();
                if (string.IsNullOrEmpty(guid))
                    continue;
                anyValidGuid = true;
                localData.RemoveTagId(guid, tagId);
            }

            if (!anyValidGuid)
                return;

            CommitChanges(saveData: true, saveList: false);
        }

        public List<string> GetTags(string guid)
        {
            if (mergedViewData == null || mergedViewList == null)
                EnsureLoaded();
            var tags = mergedViewData?.GetTags(guid, mergedViewList) ?? new List<string>();
            if (tags.Count == 0 || hiddenTags == null || hiddenTags.Count == 0)
                return tags;
            tags.RemoveAll(IsLocallyHiddenByTagName);
            return tags;
        }

        public List<string> GetAllAvailableTags()
        {
            if (localList == null || mergedViewList == null)
                EnsureLoaded();
            if (mergedViewList == null)
                return localList?.GetAvailableTags() ?? new List<string>();

            var result = mergedViewList.GetAvailableTags();
            if (localList == null)
                return result;

            var seen = new HashSet<string>(result, TagComparer);
            foreach (var n in localList.GetAvailableTags())
            {
                if (seen.Add(n))
                    result.Add(n);
            }

            if (hiddenTags != null && hiddenTags.Count > 0)
                result.RemoveAll(IsLocallyHiddenByTagName);

            return result;
        }

        public void DeleteTag(string tag)
        {
            if (localData == null || localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;

            var hasTagId = TryGetTagId(normalizedTag, out var tagId, ensureLocalById: true);

            foreach (var row in localData.assetTags.ToArray())
            {
                if (row == null || string.IsNullOrEmpty(row.guid))
                    continue;
                if (hasTagId)
                    localData.RemoveTagId(row.guid, tagId);
                else
                    localData.RemoveTag(row.guid, normalizedTag, localList);
            }

            if (hasTagId)
            {
                var id = tagId.Trim();
                AddHiddenTag(id);
                SaveHiddenTags();

                if (localList.TryFind(id, out _))
                {
                    localList.tags.RemoveAll(x =>
                        x != null
                        && AssetTagsTagId.IsWellFormed(x.tagId)
                        && string.Equals(x.tagId, id, StringComparison.OrdinalIgnoreCase));
                }
            }
            else
            {
                localList.RemoveTag(normalizedTag);
            }

            CommitChanges(saveData: true, saveList: true);
        }

        public bool CanDeleteTagFromCurrentClient(string tag)
        {
            if (localList == null || mergedViewList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return false;
            return TryGetTagId(normalizedTag, out _, ensureLocalById: false);
        }

        public void RenameTag(string oldTag, string newTag)
        {
            if (localData == null || localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(oldTag, out var normalizedOldTag) ||
                !TryNormalizeTag(newTag, out var normalizedNewTag))
                return;
            if (TagComparer.Equals(normalizedOldTag, normalizedNewTag))
                return;

            SyncMissingFromMerged();

            var oldTagId = string.Empty;
            if (!localList.TryGetId(normalizedOldTag, out oldTagId)
                && (mergedViewList == null || !mergedViewList.TryGetId(normalizedOldTag, out oldTagId)))
                return;

            if (MergedHasTag(normalizedNewTag))
            {
                if (!(localList.TryFind(oldTagId, out var existing)
                      && existing != null
                      && !string.IsNullOrWhiteSpace(existing.tagName)
                      && TagComparer.Equals(existing.tagName.Trim(), normalizedNewTag)))
                    return;
            }

            EnsureLocalRowForMutation(normalizedOldTag);
            EnsureLocalRowById(oldTagId);

            localList.RenameTag(normalizedOldTag, normalizedNewTag);
            if (localList.TryFind(oldTagId, out var row)
                && row != null
                && !string.IsNullOrWhiteSpace(row.tagName)
                && !TagComparer.Equals(row.tagName.Trim(), normalizedNewTag))
            {
                row.tagName = normalizedNewTag;
                row.StampTag();
            }
            CommitChanges(saveData: false, saveList: true);
        }

        void ReorderDataByList()
        {
            localData.ReorderAllTagsByGlobalOrder(localList, localList.GetAvailableTags());
        }

        void SaveListAndData()
        {
            CommitChanges(saveData: true, saveList: true);
        }

        public void ReorderTags(IReadOnlyList<string> orderedTagNames)
        {
            if (orderedTagNames == null || orderedTagNames.Count == 0)
                return;
            if (localData == null || localList == null)
                EnsureLoaded();
            var ordered = new List<string>();
            var seen = new HashSet<string>(TagComparer);
            foreach (var raw in orderedTagNames)
            {
                if (!TryNormalizeTag(raw, out var nm))
                    continue;
                if (!seen.Add(nm))
                    continue;
                ordered.Add(nm);
            }

            if (ordered.Count == 0)
                return;

            foreach (var nm in ordered)
            {
                if (!localList.Contains(nm))
                    EnsureLocalRowForMutation(nm);
            }

            localList.ReorderByNames(ordered);
            ReorderDataByList();
            CommitChanges(saveData: true, saveList: true);
        }

        public void MoveTagWithinOrder(string movedTag, IReadOnlyList<string> orderedTagNames)
        {
            if (orderedTagNames == null || orderedTagNames.Count == 0)
                return;
            if (localData == null || localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(movedTag, out var normalizedMovedTag))
                return;

            var ordered = new List<string>();
            var seen = new HashSet<string>(TagComparer);
            var movedIndex = -1;
            foreach (var raw in orderedTagNames)
            {
                if (!TryNormalizeTag(raw, out var nm))
                    continue;
                if (!seen.Add(nm))
                    continue;
                if (TagComparer.Equals(nm, normalizedMovedTag))
                    movedIndex = ordered.Count;
                ordered.Add(nm);
            }

            if (movedIndex < 0)
                return;

            var previousTag = movedIndex > 0 ? ordered[movedIndex - 1] : null;
            var nextTag = movedIndex < ordered.Count - 1 ? ordered[movedIndex + 1] : null;
            var previousOrderKey = TryGetOrderKey(previousTag, out var prevKey) ? prevKey : null;
            var nextOrderKey = TryGetOrderKey(nextTag, out var nextKey) ? nextKey : null;

            if (EnsureLocalRowForOrder(normalizedMovedTag)
                && localList.MoveTagBetweenOrderKeys(normalizedMovedTag, previousOrderKey, nextOrderKey, movedIndex))
            {
                CommitChanges(saveData: false, saveList: true);
                return;
            }

            EnsureLocalRowForMutation(normalizedMovedTag);
            localList.MoveTagBetweenNames(normalizedMovedTag, previousTag, nextTag);
            CommitChanges(saveData: false, saveList: true);
        }

        public void MoveTagUp(string tag)
        {
            if (localData == null || localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;
            SyncMissingFromMerged();
            EnsureLocalRowForMutation(normalizedTag);
            localList.MoveTagUp(normalizedTag);
            ReorderDataByList();
            SaveListAndData();
        }

        public void MoveTagDown(string tag)
        {
            if (localData == null || localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;
            SyncMissingFromMerged();
            EnsureLocalRowForMutation(normalizedTag);
            localList.MoveTagDown(normalizedTag);
            ReorderDataByList();
            SaveListAndData();
        }

        public Color GetTagColor(string tag)
        {
            if (mergedViewList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return new Color(0.19f, 0.38f, 0.77f, 1f);
            if (mergedViewList.TryGetTagColor(normalizedTag, out var c))
                return c;
            return new Color(0.19f, 0.38f, 0.77f, 1f);
        }

        public void SetTagColor(string tag, Color color)
        {
            if (localList == null)
                EnsureLoaded();
            if (!TryNormalizeTag(tag, out var normalizedTag))
                return;
            EnsureLocalRowForMutation(normalizedTag);
            localList.SetTagColor(normalizedTag, color);
            CommitChanges(saveData: false, saveList: true);
        }

        public void ConvertTagsToAssetLabels()
        {
            if (mergedViewData == null || mergedViewList == null)
                EnsureLoaded();

            if (mergedViewData?.assetTags == null || mergedViewData.assetTags.Count == 0)
                return;

            var updated = 0;
            foreach (var row in mergedViewData.assetTags)
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
                var fromAssetTags = row.GetResolvedTagNames(mergedViewList)
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
            }

            if (updated == 0)
                return;
        }

        public void ConvertAssetLabelsToTags()
        {
            if (localData == null || localList == null)
                EnsureLoaded();

            var changedData = false;
            var changedList = false;
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

                    var beforeTags = localList.GetAvailableTags().Count;
                    localList.AddTag(label);
                    var addedToList = localList.GetAvailableTags().Count != beforeTags;

                    var beforeCount = localData.GetTags(guid, localList).Count;
                    localData.AddTag(guid, label, localList);
                    var addedOnAsset = localData.GetTags(guid, localList).Count != beforeCount;
                    if (addedOnAsset)
                        changedData = true;

                    if (addedToList)
                        changedList = true;
                }
            }

            if (changedData || changedList)
                CommitChanges(saveData: changedData, saveList: changedList);
            else
                NotifyChanged();
        }

        public static class TagSortOrder
        {
            public const ulong KeyStep = 0x0000100000000000UL;

            public static bool TryParseOrderKey(string raw, out ulong value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(raw))
                    return false;
                var s = raw.Trim();
                if (s.Length > 16)
                    return false;
                return ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
            }

            public static string FormatOrderKey(ulong value) =>
                value.ToString("x16", CultureInfo.InvariantCulture);

            public static int CompareOrderKeys(string a, string b)
            {
                var ha = TryParseOrderKey(a, out var ua);
                var hb = TryParseOrderKey(b, out var ub);
                if (ha && hb)
                {
                    var c = ua.CompareTo(ub);
                    if (c != 0)
                        return c;
                }
                else if (ha != hb)
                    return ha ? -1 : 1;

                return string.CompareOrdinal(a ?? string.Empty, b ?? string.Empty);
            }

            public static void SortTagsListInPlace(AssetTagsList list)
            {
                if (list?.tags == null || list.tags.Count <= 1)
                    return;
                list.tags.Sort(CompareTagInfos);
            }

            static int CompareTagInfos(AssetTagsList.TagInfo a, AssetTagsList.TagInfo b)
            {
                if (a == null && b == null)
                    return 0;
                if (a == null)
                    return 1;
                if (b == null)
                    return -1;
                var c = CompareOrderKeys(a.orderKey, b.orderKey);
                if (c != 0)
                    return c;
                return string.CompareOrdinal(StableEntryIdForSort(a), StableEntryIdForSort(b));
            }

            static string StableEntryIdForSort(AssetTagsList.TagInfo t)
            {
                if (t != null && AssetTagsTagId.IsWellFormed(t.tagId))
                    return t.tagId.Trim();
                return t?.tagName?.Trim() ?? string.Empty;
            }

            public static void AssignSequentialKeysFromPhysicalOrder(AssetTagsList list)
            {
                if (list?.tags == null)
                    return;
                ulong pos = 0x1000UL;
                for (var i = 0; i < list.tags.Count; i++)
                {
                    var t = list.tags[i];
                    if (t == null || string.IsNullOrWhiteSpace(t.tagName))
                        continue;
                    if (!TryParseOrderKey(t.orderKey, out _))
                    {
                        t.orderKey = FormatOrderKey(pos);
                        if (string.IsNullOrWhiteSpace(t.orderUpdatedAt))
                            t.orderUpdatedAt = null;
                    }

                    pos += KeyStep;
                }
            }

            public static void StampNewTagAtEnd(AssetTagsList list, AssetTagsList.TagInfo tag)
            {
                if (list?.tags == null || tag == null)
                    return;
                ulong max = 0;
                var any = false;
                for (var i = 0; i < list.tags.Count; i++)
                {
                    var t = list.tags[i];
                    if (t == null || string.IsNullOrWhiteSpace(t.tagName) || ReferenceEquals(t, tag))
                        continue;
                    if (TryParseOrderKey(t.orderKey, out var v))
                    {
                        any = true;
                        if (v > max)
                            max = v;
                    }
                }

                tag.orderKey = FormatOrderKey(any ? max + KeyStep : 0x1000UL);
                tag.StampOrderUpdate();
            }

            public static void RepairKeysAfterAdjacentSwap(AssetTagsList list, int lowIndex, int highIndex)
            {
                if (list?.tags == null || lowIndex < 0 || highIndex >= list.tags.Count || lowIndex >= highIndex)
                    return;

                if (!EnsureAllKeysValid(list))
                {
                    AssignSequentialKeysFromPhysicalOrder(list);
                    SortTagsListInPlace(list);
                    return;
                }

                for (var attempt = 0; attempt < 48; attempt++)
                {
                    if (TryRepairPair(list, lowIndex, highIndex))
                    {
                        SortTagsListInPlace(list);
                        return;
                    }

                    RedistributeKeysDense(list);
                }

                AssignSequentialKeysFromPhysicalOrder(list);
                SortTagsListInPlace(list);
            }

            static bool EnsureAllKeysValid(AssetTagsList list)
            {
                for (var i = 0; i < list.tags.Count; i++)
                {
                    var t = list.tags[i];
                    if (t == null || string.IsNullOrWhiteSpace(t.tagName))
                        continue;
                    if (!TryParseOrderKey(t.orderKey, out _))
                        return false;
                }

                return true;
            }

            static bool TryRepairPair(AssetTagsList list, int lowIndex, int highIndex)
            {
                ulong prev = 0UL;
                if (lowIndex > 0)
                {
                    var p = list.tags[lowIndex - 1];
                    if (p == null || !TryParseOrderKey(p.orderKey, out prev))
                        return false;
                }

                ulong next = ulong.MaxValue;
                if (highIndex < list.tags.Count - 1)
                {
                    var n = list.tags[highIndex + 1];
                    if (n == null || !TryParseOrderKey(n.orderKey, out next))
                        return false;
                }

                var a = list.tags[lowIndex];
                var b = list.tags[highIndex];
                if (a == null || b == null)
                    return false;

                if (prev >= next)
                    return false;
                var span = next - prev;
                if (span < 4UL)
                    return false;
                var k1 = prev + span / 3UL;
                var k2 = prev + (span * 2UL) / 3UL;
                if (k1 <= prev || k2 <= k1 || k2 >= next)
                    return false;

                a.orderKey = FormatOrderKey(k1);
                a.StampOrderUpdate();
                b.orderKey = FormatOrderKey(k2);
                b.StampOrderUpdate();
                return true;
            }

            static void RedistributeKeysDense(AssetTagsList list)
            {
                if (list?.tags == null)
                    return;
                var active = new List<AssetTagsList.TagInfo>();
                foreach (var t in list.tags)
                {
                    if (t != null && !string.IsNullOrWhiteSpace(t.tagName))
                        active.Add(t);
                }

                ulong pos = 0x1000UL;
                for (var i = 0; i < active.Count; i++)
                {
                    active[i].orderKey = FormatOrderKey(pos);
                    active[i].StampOrderUpdate();
                    pos += KeyStep / 256UL;
                    if (pos == 0UL)
                        pos += KeyStep;
                }
            }

            public static bool IncomingOrderKeyWins(
                AssetTagsList.TagInfo existing,
                AssetTagsList.TagInfo incoming,
                bool preferIncomingOnTimestampTie)
            {
                if (incoming == null)
                    return false;
                if (existing == null)
                    return true;
                if (AssetTagsJsonRepository.IsUtcStrictlyNewerThan(
                        incoming.orderUpdatedAt,
                        existing.orderUpdatedAt))
                    return true;
                if (!preferIncomingOnTimestampTie)
                    return false;
                return !AssetTagsJsonRepository.IsUtcStrictlyNewerThan(
                    existing.orderUpdatedAt,
                    incoming.orderUpdatedAt);
            }

            public static void ApplyIncomingTagEntry(AssetTagsList.TagInfo target, AssetTagsList.TagInfo incoming)
            {
                if (target == null || incoming == null)
                    return;
                target.tagName = incoming.tagName.Trim();
                target.color = incoming.color;
                target.tagUpdatedAt = incoming.tagUpdatedAt;
                target.tagUpdatedBy = incoming.tagUpdatedBy;
                if (AssetTagsTagId.IsWellFormed(incoming.tagId))
                    target.tagId = incoming.tagId.Trim();
                else if (!AssetTagsTagId.IsWellFormed(target.tagId))
                    target.tagId = AssetTagsTagId.NewTagId();
                target.order = incoming.order;
            }

            public static string[] BuildOrderIdArray(AssetTagsList list)
            {
                if (list?.tags == null)
                    return Array.Empty<string>();
                var tmp = list.tags.Where(t => t != null && AssetTagsTagId.IsWellFormed(t.tagId)).ToList();
                tmp.Sort(CompareTagInfos);
                var ids = new string[tmp.Count];
                for (var i = 0; i < tmp.Count; i++)
                    ids[i] = tmp[i].tagId.Trim();
                return ids;
            }
        }
    }

    [InitializeOnLoad]
    static class AssetTagsRootFolderPathLifecycle
    {
        static AssetTagsRootFolderPathLifecycle() =>
            EditorApplication.projectChanged += AssetTagsManager.InvalidateRootFolderPathCache;
    }
}
#endif
