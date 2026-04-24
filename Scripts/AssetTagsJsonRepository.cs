#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public static class AssetTagsJsonRepository
    {
        const string GlobalAssetTagsFileName = "AssetTagsData.json";
        const string GlobalAssetTagListFileName = "AssetTagsList.json";

        const string LocalDataFolderUnderRoot = "Data";
        const string LocalDataAssetTagsPrefix = "AssetTagsData_";
        const string LocalDataAssetListPrefix = "AssetTagsList_";

        static int readListRecursionDepth;

        static string ProjectRootFull =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static string UnityLibraryFull =>
            Path.GetFullPath(Path.Combine(ProjectRootFull, "Library"));

        static string GlobalDataDirFull =>
            Path.Combine(UnityLibraryFull, "INDiEA", "Asset Tags", "Data");

        public static string LocalDataFolderAssetPath =>
            $"{AssetTagsManager.RootFolderPath}/{LocalDataFolderUnderRoot}";

        static string LocalDataFolderFull =>
            AssetTagsManager.AssetPathToFullPathOnDisk(
                $"{AssetTagsManager.RootFolderPath}/{LocalDataFolderUnderRoot}");

        public static string GetLocalAssetTagsJsonFullPath(string workstationToken) =>
            Path.Combine(LocalDataFolderFull, $"{LocalDataAssetTagsPrefix}{workstationToken}.json");

        public static string GetLocalAssetTagListJsonFullPath(string workstationToken) =>
            Path.Combine(LocalDataFolderFull, $"{LocalDataAssetListPrefix}{workstationToken}.json");

        static string GlobalAssetTagsFullPath =>
            Path.Combine(GlobalDataDirFull, GlobalAssetTagsFileName);

        static string GlobalAssetTagListFullPath =>
            Path.Combine(GlobalDataDirFull, GlobalAssetTagListFileName);


        public static void LoadGlobalDataCacheInto(AssetTagsData target, AssetTagsList tagList)
        {
            target.assetTags.Clear();
            if (tagList == null)
                return;
            tagList.tags.Clear();
            TryReadList(GlobalAssetTagListFullPath, tagList);
            tagList.FillMissingIds();
            TryReadData(GlobalAssetTagsFullPath, target);
        }

        [Serializable]
        class DataFileDto
        {
            public List<DataRowDto> assetTags = new List<DataRowDto>();
        }

        [Serializable]
        class DataRowDto
        {
            public string guid;
            public List<DataTagDto> tags = new List<DataTagDto>();
        }

        [Serializable]
        class DataTagDto
        {
            public string tagId;
            public string linkUpdatedAt;
            public string linkUpdatedBy;
        }

        [Serializable]
        class ListFileDto
        {
            public List<ListEntryDto> tags = new List<ListEntryDto>();
            public List<HiddenTagDto> hiddenTags = new List<HiddenTagDto>();
        }

        [Serializable]
        class HiddenTagDto
        {
            public string tagId;
            public string hiddenAt;
            public string hiddenBy;
        }

        public sealed class HiddenTag
        {
            public string tagId;
            public string hiddenAt;
            public string hiddenBy;

            public HiddenTag(string tagId, string hiddenAt, string hiddenBy)
            {
                this.tagId = tagId;
                this.hiddenAt = hiddenAt;
                this.hiddenBy = hiddenBy;
            }
        }

        [Serializable]
        class ListEntryDto
        {
            public string tagId;
            public string tagName;
            public float colorR;
            public float colorG;
            public float colorB;
            public float colorA = 1f;
            public string tagUpdatedAt;
            public string tagUpdatedBy;
            public string orderKey;
            public string orderUpdatedAt;
            public string orderUpdatedBy;

            public int order;
        }

        static List<HiddenTag> NormalizeHiddenTags(IEnumerable<HiddenTagDto> records)
        {
            var byId = new Dictionary<string, HiddenTag>(StringComparer.OrdinalIgnoreCase);

            if (records != null)
            {
                foreach (var record in records)
                {
                    if (!AssetTagsTagId.IsWellFormed(record?.tagId))
                        continue;
                    var id = record.tagId.Trim();
                    var next = new HiddenTag(
                        id,
                        string.IsNullOrWhiteSpace(record.hiddenAt) ? null : record.hiddenAt,
                        string.IsNullOrWhiteSpace(record.hiddenBy) ? null : record.hiddenBy);
                    if (!byId.TryGetValue(id, out var existing)
                        || IsUtcStrictlyNewerThan(next.hiddenAt, existing.hiddenAt)
                        || !IsUtcStrictlyNewerThan(existing.hiddenAt, next.hiddenAt))
                        byId[id] = next;
                }
            }

            var result = byId.Values.ToList();
            result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.tagId, b.tagId));
            return result;
        }

        static List<HiddenTag> ReadHiddenTagsFromListFile(string fullPath)
        {
            if (!File.Exists(fullPath))
                return new List<HiddenTag>();
            try
            {
                var dto = JsonUtility.FromJson<ListFileDto>(File.ReadAllText(fullPath));
                return NormalizeHiddenTags(dto?.hiddenTags);
            }
            catch
            {
                return new List<HiddenTag>();
            }
        }

        public static bool IsRunningOnAssetImportWorker =>
            AssetDatabase.IsAssetImportWorkerProcess();

        public static void EnsureInfrastructure()
        {
            Directory.CreateDirectory(GlobalDataDirFull);
            WriteTextIfMissing(GlobalAssetTagsFullPath, JsonUtility.ToJson(new DataFileDto(), true));
            WriteTextIfMissing(GlobalAssetTagListFullPath, JsonUtility.ToJson(new ListFileDto(), true));

            if (IsRunningOnAssetImportWorker)
            {
                try
                {
                    var rootDisk = AssetTagsManager.AssetPathToFullPathOnDisk(AssetTagsManager.RootFolderPath);
                    if (!string.IsNullOrEmpty(rootDisk))
                        Directory.CreateDirectory(rootDisk);
                    if (!string.IsNullOrEmpty(LocalDataFolderFull))
                        Directory.CreateDirectory(LocalDataFolderFull);
                }
                catch{}
            }
            else
            {
                EnsureRootFolderExists();
                if (!AssetDatabase.IsValidFolder(LocalDataFolderAssetPath))
                    AssetDatabase.CreateFolder(AssetTagsManager.RootFolderPath, LocalDataFolderUnderRoot);
            }

            var token = AssetTagsClientId.GetOrCreateClientId();
            WorkstationTokenCached = token;
            var dataPath = GetLocalAssetTagsJsonFullPath(token);
            var listPath = GetLocalAssetTagListJsonFullPath(token);

            var localDataExists = File.Exists(dataPath);
            var localListExists = File.Exists(listPath);
            if (localDataExists && localListExists)
                return;

            var data = new AssetTagsData();
            var list = new AssetTagsList();

            if (localListExists)
                TryReadList(listPath, list);
            list.FillMissingIds();

            if (localDataExists)
                TryReadData(dataPath, data);
            SaveDataState(dataPath, data);
            SaveListState(listPath, list);
        }

        static void EnsureRootFolderExists() =>
            AssetTagsManager.EnsureRootFolderExists();

        static void WriteTextIfMissing(string fullPath, string contents)
        {
            if (File.Exists(fullPath))
                return;
            try
            {
                WriteAllTextAtomic(fullPath, contents, overwrite: false);
            }
            catch (IOException)
            {
                if (!File.Exists(fullPath))
                    throw;
            }
        }

        static void WriteAllTextAtomic(string fullPath, string contents, bool overwrite = true)
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tempPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tempPath, contents);
                const int maxAttempts = 3;
                for (var attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        if (!File.Exists(fullPath))
                        {
                            File.Move(tempPath, fullPath);
                            return;
                        }

                        if (!overwrite)
                            return;

                        try
                        {
                            File.Replace(tempPath, fullPath, null, true);
                            return;
                        }
                        catch (IOException)
                        {
                            File.Copy(tempPath, fullPath, true);
                            return;
                        }
                    }
                    catch (IOException) when (attempt < maxAttempts - 1)
                    {
                        Thread.Sleep(15 * (attempt + 1));
                    }
                    catch (UnauthorizedAccessException) when (attempt < maxAttempts - 1)
                    {
                        Thread.Sleep(15 * (attempt + 1));
                    }
                }

                if (File.Exists(fullPath))
                {
                    if (!overwrite)
                        return;
                    File.Copy(tempPath, fullPath, true);
                    return;
                }

                File.Move(tempPath, fullPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch {}
            }
        }

        public static void LoadLocal(string workstationToken, AssetTagsData data, AssetTagsList list)
        {
            data.assetTags.Clear();
            list.tags.Clear();

            var dataPath = GetLocalAssetTagsJsonFullPath(workstationToken);
            var listPath = GetLocalAssetTagListJsonFullPath(workstationToken);

            TryReadList(listPath, list);
            list.FillMissingIds();
            TryReadData(dataPath, data);
        }

        public static void LoadGlobal(AssetTagsData data, AssetTagsList list)
        {
            data.assetTags.Clear();
            list.tags.Clear();

            TryReadList(GlobalAssetTagListFullPath, list);
            list.FillMissingIds();
            TryReadData(GlobalAssetTagsFullPath, data);
        }

        public static void RebuildMergedFromAllLocalFilesAndGlobalCache(
            out AssetTagsData mergedData,
            out AssetTagsList mergedList)
        {
            mergedData = new AssetTagsData();
            mergedList = new AssetTagsList();

            if (Directory.Exists(LocalDataFolderFull))
            {
                var listChunks = new List<(string path, AssetTagsList chunk, DateTime maxRowUtc, DateTime fileUtc)>();
                var hiddenTags = new Dictionary<string, HiddenTag>(StringComparer.OrdinalIgnoreCase);
                foreach (var fullPath in Directory.GetFiles(LocalDataFolderFull, LocalDataAssetListPrefix + "*.json"))
                {
                    var chunk = new AssetTagsList();
                    TryReadList(fullPath, chunk);
                    listChunks.Add((fullPath, chunk, GetListMaxValidModifiedUtc(chunk), SafeGetLastWriteTimeUtc(fullPath)));
                    MergeHiddenTagsInto(hiddenTags, ReadHiddenTagsFromListFile(fullPath));
                }

                listChunks.Sort((a, b) => CompareLocalJsonMergeOrder(a.maxRowUtc, a.fileUtc, a.path, b.maxRowUtc, b.fileUtc, b.path));
                for (var i = 0; i < listChunks.Count; i++)
                    PreferNewerMergeListInto(mergedList, listChunks[i].chunk);
                mergedList.FillMissingIds();

                var dataChunks = new List<(string path, AssetTagsData chunk, DateTime maxRowUtc, DateTime fileUtc)>();
                foreach (var fullPath in Directory.GetFiles(LocalDataFolderFull, LocalDataAssetTagsPrefix + "*.json"))
                {
                    var chunk = new AssetTagsData();
                    TryReadData(fullPath, chunk);
                    dataChunks.Add((fullPath, chunk, GetDataMaxValidModifiedUtc(chunk), SafeGetLastWriteTimeUtc(fullPath)));
                }

                dataChunks.Sort((a, b) => CompareLocalJsonMergeOrder(a.maxRowUtc, a.fileUtc, a.path, b.maxRowUtc, b.fileUtc, b.path));
                for (var i = 0; i < dataChunks.Count; i++)
                    PreferNewerMergeDataInto(mergedData, dataChunks[i].chunk);

                if (AssetTagsManager.IsDeletedTagRecordMergeEnabled())
                    ApplyHiddenTagsToMerged(mergedData, mergedList, hiddenTags);
            }

            mergedList.FillMissingIds();
            AssetTagsManager.TagSortOrder.AssignSequentialKeysFromPhysicalOrder(mergedList);
            AssetTagsManager.TagSortOrder.SortTagsListInPlace(mergedList);

            SaveDataState(GlobalAssetTagsFullPath, mergedData);
            SaveListState(GlobalAssetTagListFullPath, mergedList);
        }

        static void MergeHiddenTagsInto(
            Dictionary<string, HiddenTag> target,
            IEnumerable<HiddenTag> incoming)
        {
            if (target == null || incoming == null)
                return;
            foreach (var record in incoming)
            {
                if (record == null || !AssetTagsTagId.IsWellFormed(record.tagId))
                    continue;
                var id = record.tagId.Trim();
                if (!target.TryGetValue(id, out var existing)
                    || IsUtcStrictlyNewerThan(record.hiddenAt, existing.hiddenAt)
                    || !IsUtcStrictlyNewerThan(existing.hiddenAt, record.hiddenAt))
                    target[id] = record;
            }
        }

        static void ApplyHiddenTagsToMerged(
            AssetTagsData mergedData,
            AssetTagsList mergedList,
            Dictionary<string, HiddenTag> hiddenTags)
        {
            if (hiddenTags == null || hiddenTags.Count == 0)
                return;

            if (mergedList?.tags != null)
            {
                mergedList.tags.RemoveAll(tag =>
                    tag != null
                    && AssetTagsTagId.IsWellFormed(tag.tagId)
                    && hiddenTags.TryGetValue(tag.tagId.Trim(), out var hidden)
                    && HiddenTagWinsOverListEntry(hidden, tag));
            }

            if (mergedData?.assetTags == null)
                return;

            foreach (var row in mergedData.assetTags)
            {
                if (row?.tags == null || row.tags.Count == 0)
                    continue;
                row.tags.RemoveAll(entry =>
                    entry != null
                    && AssetTagsTagId.IsWellFormed(entry.tagId)
                    && hiddenTags.TryGetValue(entry.tagId.Trim(), out var hidden)
                    && HiddenTagWinsOverDataEntry(hidden, entry));
            }

            mergedData.assetTags.RemoveAll(row => row?.tags == null || row.tags.Count == 0);
        }

        static bool HiddenTagWinsOverListEntry(HiddenTag hidden, AssetTagsList.TagInfo tag)
        {
            if (hidden == null || tag == null || string.IsNullOrWhiteSpace(hidden.hiddenAt))
                return false;
            return IsUtcStrictlyNewerThan(hidden.hiddenAt, tag.tagUpdatedAt);
        }

        static bool HiddenTagWinsOverDataEntry(HiddenTag hidden, AssetTagsData.AssetTagEntry entry)
        {
            if (hidden == null || entry == null || string.IsNullOrWhiteSpace(hidden.hiddenAt))
                return false;
            return IsUtcStrictlyNewerThan(hidden.hiddenAt, entry.linkUpdatedAt);
        }


        static DateTime SafeGetLastWriteTimeUtc(string fullPath)
        {
            try
            {
                return !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath)
                    ? File.GetLastWriteTimeUtc(fullPath)
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        static DateTime GetDataMaxValidModifiedUtc(AssetTagsData data)
        {
            var max = DateTime.MinValue;
            var any = false;
            if (data?.assetTags == null)
                return DateTime.MinValue;
            foreach (var row in data.assetTags)
            {
                if (row?.tags == null)
                    continue;
                for (var i = 0; i < row.tags.Count; i++)
                {
                    var e = row.tags[i];
                    if (e == null || !AssetTagsTagId.IsWellFormed(e.tagId) || string.IsNullOrWhiteSpace(e.linkUpdatedAt))
                        continue;
                    if (!TryParseModifiedUtc(e.linkUpdatedAt, out var u))
                        continue;
                    if (!any || u > max)
                        max = u;
                    any = true;
                }
            }

            return any ? max : DateTime.MinValue;
        }

        static DateTime GetListMaxValidModifiedUtc(AssetTagsList list)
        {
            var max = DateTime.MinValue;
            var any = false;
            if (list?.tags == null)
                return DateTime.MinValue;
            for (var i = 0; i < list.tags.Count; i++)
            {
                var t = list.tags[i];
                if (t == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(t.tagUpdatedAt)
                    && TryParseModifiedUtc(t.tagUpdatedAt, out var uDef))
                {
                    if (!any || uDef > max)
                        max = uDef;
                    any = true;
                }

                if (!string.IsNullOrWhiteSpace(t.orderUpdatedAt)
                    && TryParseModifiedUtc(t.orderUpdatedAt, out var uSort))
                {
                    if (!any || uSort > max)
                        max = uSort;
                    any = true;
                }
            }

            return any ? max : DateTime.MinValue;
        }

        static int CompareLocalJsonMergeOrder(
            DateTime maxUtcA,
            DateTime fileUtcA,
            string pathA,
            DateTime maxUtcB,
            DateTime fileUtcB,
            string pathB)
        {
            var c = maxUtcA.CompareTo(maxUtcB);
            if (c != 0)
                return c;
            c = fileUtcA.CompareTo(fileUtcB);
            if (c != 0)
                return c;
            return StringComparer.OrdinalIgnoreCase.Compare(pathA ?? string.Empty, pathB ?? string.Empty);
        }

        static bool TryParseModifiedUtc(string raw, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            return DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out utc);
        }

        public static bool IsUtcStrictlyNewerThan(string incomingUtc, string existingUtc)
        {
            var inc = TryParseModifiedUtc(incomingUtc, out var iT);
            var ex = TryParseModifiedUtc(existingUtc, out var eT);
            if (inc && ex)
                return iT > eT;
            if (inc && !ex)
                return true;
            if (!inc && ex)
                return false;
            return false;
        }

        static bool MergeIncomingDataEntryWins(
            AssetTagsData.AssetTagEntry existing,
            AssetTagsData.AssetTagEntry incoming,
            bool preferIncomingOnTimestampTie)
        {
            if (incoming == null)
                return false;
            if (existing == null)
                return true;
            if (IsUtcStrictlyNewerThan(incoming.linkUpdatedAt, existing.linkUpdatedAt))
                return true;
            if (!preferIncomingOnTimestampTie)
                return false;
            return !IsUtcStrictlyNewerThan(existing.linkUpdatedAt, incoming.linkUpdatedAt);
        }

        static bool MergeIncomingListEntryWins(
            AssetTagsList.TagInfo existing,
            AssetTagsList.TagInfo incoming,
            bool preferIncomingOnTimestampTie)
        {
            if (incoming == null)
                return false;
            if (existing == null)
                return true;
            if (IsUtcStrictlyNewerThan(incoming.tagUpdatedAt, existing.tagUpdatedAt))
                return true;
            if (!preferIncomingOnTimestampTie)
                return false;
            return !IsUtcStrictlyNewerThan(existing.tagUpdatedAt, incoming.tagUpdatedAt);
        }

        static void PreferNewerMergeDataInto(
            AssetTagsData target,
            AssetTagsData incoming,
            bool addMissing = true,
            bool preferIncomingOnTimestampTie = true)
        {
            if (incoming?.assetTags == null)
                return;
            foreach (var row in incoming.assetTags)
            {
                if (row == null || string.IsNullOrEmpty(row.guid) || row.tags == null)
                    continue;
                var guid = row.guid.Trim();
                foreach (var e in row.tags)
                {
                    if (e == null || !AssetTagsTagId.IsWellFormed(e.tagId))
                        continue;
                    var tagId = e.tagId.Trim();
                    var trow = target.assetTags.Find(x =>
                        x != null && string.Equals(x.guid, guid, StringComparison.OrdinalIgnoreCase));
                    AssetTagsData.AssetTagEntry existingEntry = null;
                    if (trow?.tags != null)
                    {
                        existingEntry = trow.tags.Find(x =>
                            x != null
                            && AssetTagsTagId.IsWellFormed(x.tagId)
                            && string.Equals(x.tagId, tagId, StringComparison.OrdinalIgnoreCase));
                    }

                    if (existingEntry == null)
                    {
                        if (!addMissing)
                            continue;
                        target.ReplaceOrAddTagEntry(guid, AssetTagsData.AssetTagEntry.Clone(e));
                    }
                    else if (MergeIncomingDataEntryWins(existingEntry, e, preferIncomingOnTimestampTie))
                        target.ReplaceOrAddTagEntry(guid, AssetTagsData.AssetTagEntry.Clone(e));
                }
            }
        }

        static void PreferNewerMergeListInto(
            AssetTagsList target,
            AssetTagsList incoming,
            bool addMissing = true,
            bool preferIncomingOnTimestampTie = true)
        {
            if (incoming == null)
                return;
            if (incoming.tags != null)
            {
                foreach (var e in incoming.tags)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.tagName))
                        continue;
                    var name = e.tagName.Trim();
                    AssetTagsList.TagInfo existing = null;
                    if (AssetTagsTagId.IsWellFormed(e.tagId))
                    {
                        var tagId = e.tagId.Trim();
                        existing = target.tags.Find(x =>
                            x != null
                            && AssetTagsTagId.IsWellFormed(x.tagId)
                            && string.Equals(x.tagId, tagId, StringComparison.OrdinalIgnoreCase));
                    }

                    if (existing == null)
                        existing = target.tags.Find(x =>
                            x != null && string.Equals(x.tagName, name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        if (!addMissing)
                            continue;
                        target.ReplaceOrAddListEntry(CloneListTagInfo(e));
                    }
                    else
                    {
                        var defWin = MergeIncomingListEntryWins(existing, e, preferIncomingOnTimestampTie);
                        var orderWin = AssetTagsManager.TagSortOrder.IncomingOrderKeyWins(
                            existing,
                            e,
                            preferIncomingOnTimestampTie);
                        if (defWin && orderWin)
                        {
                            var c = CloneListTagInfo(e);
                            if (!AssetTagsTagId.IsWellFormed(c.tagId) && AssetTagsTagId.IsWellFormed(existing.tagId))
                                c.tagId = existing.tagId.Trim();
                            target.ReplaceOrAddListEntry(c);
                        }
                        else if (defWin && !orderWin)
                        {
                            var c = CloneListTagInfo(e);
                            if (!AssetTagsTagId.IsWellFormed(c.tagId) && AssetTagsTagId.IsWellFormed(existing.tagId))
                                c.tagId = existing.tagId.Trim();
                            c.orderKey = existing.orderKey;
                            c.orderUpdatedAt = existing.orderUpdatedAt;
                            c.orderUpdatedBy = existing.orderUpdatedBy;
                            c.order = existing.order;
                            target.ReplaceOrAddListEntry(c);
                        }
                        else if (!defWin && orderWin)
                        {
                            if (!string.IsNullOrWhiteSpace(e.orderKey))
                                existing.orderKey = e.orderKey.Trim();
                            if (!string.IsNullOrWhiteSpace(e.orderUpdatedAt))
                                existing.orderUpdatedAt = e.orderUpdatedAt;
                            if (!string.IsNullOrWhiteSpace(e.orderUpdatedBy))
                                existing.orderUpdatedBy = e.orderUpdatedBy;
                            existing.order = e.order;
                        }
                    }
                }
            }
        }

        public static void MergePreferNewerData(AssetTagsData local, AssetTagsData global, AssetTagsData target)
        {
            target.assetTags.Clear();
            if (global?.assetTags != null)
            {
                foreach (var row in global.assetTags)
                {
                    if (row == null || string.IsNullOrEmpty(row.guid) || row.tags == null)
                        continue;
                    foreach (var e in row.tags)
                    {
                        if (e == null || !AssetTagsTagId.IsWellFormed(e.tagId))
                            continue;
                        target.ReplaceOrAddTagEntry(row.guid.Trim(), AssetTagsData.AssetTagEntry.Clone(e));
                    }
                }
            }

            if (local?.assetTags == null)
                return;
            foreach (var row in local.assetTags)
            {
                if (row == null || string.IsNullOrEmpty(row.guid) || row.tags == null)
                    continue;
                foreach (var e in row.tags)
                {
                    if (e == null || !AssetTagsTagId.IsWellFormed(e.tagId))
                        continue;
                    target.ReplaceOrAddTagEntry(row.guid.Trim(), AssetTagsData.AssetTagEntry.Clone(e));
                }
            }
        }

        static AssetTagsList.TagInfo CloneListTagInfo(AssetTagsList.TagInfo e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.tagName))
                return null;
            return new AssetTagsList.TagInfo(e.tagName.Trim(), e.color)
            {
                tagId = AssetTagsTagId.IsWellFormed(e.tagId) ? e.tagId.Trim() : null,
                tagUpdatedAt = e.tagUpdatedAt,
                tagUpdatedBy = e.tagUpdatedBy,
                orderKey = string.IsNullOrWhiteSpace(e.orderKey) ? null : e.orderKey.Trim(),
                orderUpdatedAt = e.orderUpdatedAt,
                orderUpdatedBy = e.orderUpdatedBy,
                order = e.order,
            };
        }

        public static void MergePreferNewerList(AssetTagsList local, AssetTagsList global, AssetTagsList target)
        {
            target.tags.Clear();
            var namesFromGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (global?.tags != null)
            {
                foreach (var ge in global.tags)
                {
                    if (ge == null || string.IsNullOrWhiteSpace(ge.tagName))
                        continue;
                    var n = ge.tagName.Trim();
                    namesFromGlobal.Add(n);
                    var le = local?.tags?.Find(x =>
                        x != null
                        && ((AssetTagsTagId.IsWellFormed(ge.tagId)
                             && AssetTagsTagId.IsWellFormed(x.tagId)
                             && string.Equals(x.tagId, ge.tagId, StringComparison.OrdinalIgnoreCase))
                            || string.Equals(x.tagName, n, StringComparison.OrdinalIgnoreCase)));
                    if (le != null)
                        target.ReplaceOrAddListEntry(CloneListTagInfo(le));
                    else
                        target.ReplaceOrAddListEntry(CloneListTagInfo(ge));
                }
            }

            if (local?.tags == null)
                return;
            foreach (var le in local.tags)
            {
                if (le == null || string.IsNullOrWhiteSpace(le.tagName))
                    continue;
                var n = le.tagName.Trim();
                if (namesFromGlobal.Contains(n))
                    continue;
                target.ReplaceOrAddListEntry(CloneListTagInfo(le));
            }
        }

        static void TryReadDataWithPerTagMeta(string text, AssetTagsData target)
        {
            var dto = JsonUtility.FromJson<DataFileDto>(text);
            if (dto?.assetTags == null)
                return;
            foreach (var row in dto.assetTags)
            {
                if (row == null || string.IsNullOrEmpty(row.guid) || row.tags == null)
                    continue;
                foreach (var t in row.tags)
                {
                    if (t == null)
                        continue;
                    if (!AssetTagsTagId.IsWellFormed(t.tagId))
                        continue;
                    target.ReplaceOrAddTagEntry(
                        row.guid.Trim(),
                        new AssetTagsData.AssetTagEntry
                        {
                            tagId = t.tagId.Trim(),
                            linkUpdatedAt = t.linkUpdatedAt,
                            linkUpdatedBy = t.linkUpdatedBy,
                        });
                }
            }
        }

        static void TryReadData(string fullPath, AssetTagsData target)
        {
            if (!File.Exists(fullPath))
                return;
            try
            {
                var text = File.ReadAllText(fullPath);
                TryReadDataWithPerTagMeta(text, target);
            }
            catch{}
            {}
        }

        static void AssignListOrderIndices(AssetTagsList list)
        {
            if (list?.tags == null)
                return;
            var rank = 0;
            for (var i = 0; i < list.tags.Count; i++)
            {
                var e = list.tags[i];
                if (e == null || string.IsNullOrWhiteSpace(e.tagName) || !AssetTagsTagId.IsWellFormed(e.tagId))
                    continue;
                e.order = rank++;
            }
        }

        static AssetTagsList.TagInfo TagInfoFromListEntryDto(ListEntryDto e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.tagName))
                return null;
            var name = e.tagName.Trim();
            if (!AssetTagsTagId.IsWellFormed(e.tagId))
                return null;
            return new AssetTagsList.TagInfo(name, new Color(e.colorR, e.colorG, e.colorB, e.colorA))
            {
                tagId = e.tagId.Trim(),
                tagUpdatedAt = e.tagUpdatedAt,
                tagUpdatedBy = e.tagUpdatedBy,
                orderKey = string.IsNullOrWhiteSpace(e.orderKey) ? null : e.orderKey.Trim(),
                orderUpdatedAt = e.orderUpdatedAt,
                orderUpdatedBy = e.orderUpdatedBy,
                order = e.order,
            };
        }

        static ListEntryDto ListEntryDtoFromTagInfo(AssetTagsList.TagInfo e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.tagName))
                return null;
            return new ListEntryDto
            {
                tagId = AssetTagsTagId.IsWellFormed(e.tagId) ? e.tagId.Trim() : null,
                tagName = e.tagName.Trim(),
                colorR = e.color.r,
                colorG = e.color.g,
                colorB = e.color.b,
                colorA = e.color.a,
                tagUpdatedAt = e.tagUpdatedAt,
                tagUpdatedBy = e.tagUpdatedBy,
                orderKey = string.IsNullOrWhiteSpace(e.orderKey) ? null : e.orderKey.Trim(),
                orderUpdatedAt = e.orderUpdatedAt,
                orderUpdatedBy = e.orderUpdatedBy,
                order = e.order,
            };
        }

        static void TryReadList(string fullPath, AssetTagsList target)
        {
            if (!File.Exists(fullPath))
                return;
            if (readListRecursionDepth > 10)
                return;
            readListRecursionDepth++;
            try
            {
                var dto = JsonUtility.FromJson<ListFileDto>(File.ReadAllText(fullPath));
                if (dto == null)
                    return;

                if (dto.tags == null)
                    return;

                var built = new List<AssetTagsList.TagInfo>();
                for (var i = 0; i < dto.tags.Count; i++)
                {
                    var ti = TagInfoFromListEntryDto(dto.tags[i]);
                    if (ti != null)
                        built.Add(ti);
                }

                var shell = new AssetTagsList { tags = built };
                shell.FillMissingIds();

                target.tags.Clear();
                var numbered = new List<(AssetTagsList.TagInfo tag, int fileIdx)>();
                for (var i = 0; i < built.Count; i++)
                {
                    var t = built[i];
                    if (t != null && !string.IsNullOrWhiteSpace(t.tagName))
                        numbered.Add((t, i));
                }

                numbered.Sort((a, b) =>
                {
                    var c = a.tag.order.CompareTo(b.tag.order);
                    if (c != 0)
                        return c;
                    return a.fileIdx.CompareTo(b.fileIdx);
                });
                for (var i = 0; i < numbered.Count; i++)
                    target.tags.Add(numbered[i].tag);

                AssetTagsManager.TagSortOrder.AssignSequentialKeysFromPhysicalOrder(target);
                AssetTagsManager.TagSortOrder.SortTagsListInPlace(target);
                AssignListOrderIndices(target);
            }
            catch{}
            finally
            {
                readListRecursionDepth--;
            }
        }

        public static void SaveDataState(string fullPath, AssetTagsData state)
        {
            var dto = new DataFileDto();
            foreach (var row in state.assetTags)
            {
                if (row == null || string.IsNullOrEmpty(row.guid) || row.tags == null)
                    continue;
                var rowDto = new DataRowDto { guid = row.guid.Trim() };
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in row.tags)
                {
                    if (t == null || !AssetTagsTagId.IsWellFormed(t.tagId))
                        continue;
                    var tagId = t.tagId.Trim();
                    if (!seenIds.Add(tagId))
                        continue;
                    rowDto.tags.Add(new DataTagDto
                    {
                        tagId = tagId,
                        linkUpdatedAt = t.linkUpdatedAt,
                        linkUpdatedBy = t.linkUpdatedBy,
                    });
                }

                if (rowDto.tags.Count == 0)
                    continue;
                dto.assetTags.Add(rowDto);
            }

            WriteAllTextAtomic(fullPath, JsonUtility.ToJson(dto, true));
            ImportIfUnderAssets(fullPath);
        }

        static void SaveListStateInternal(string fullPath, AssetTagsList state, IEnumerable<HiddenTag> hiddenTags)
        {
            AssetTagsManager.TagSortOrder.SortTagsListInPlace(state);
            AssetTagsManager.TagSortOrder.AssignSequentialKeysFromPhysicalOrder(state);
            AssignListOrderIndices(state);

            var dto = new ListFileDto();
            foreach (var e in state.tags)
            {
                var rowDto = ListEntryDtoFromTagInfo(e);
                if (rowDto != null)
                    dto.tags.Add(rowDto);
            }
            dto.hiddenTags = NormalizeHiddenTags(ToHiddenTagDtos(hiddenTags))
                .Select(x => new HiddenTagDto
                {
                    tagId = x.tagId,
                    hiddenAt = x.hiddenAt,
                    hiddenBy = x.hiddenBy,
                })
                .ToList();

            WriteAllTextAtomic(fullPath, JsonUtility.ToJson(dto, true));
            ImportIfUnderAssets(fullPath);
        }

        static IEnumerable<HiddenTagDto> ToHiddenTagDtos(IEnumerable<HiddenTag> records)
        {
            if (records == null)
                yield break;
            foreach (var record in records)
            {
                if (record == null || !AssetTagsTagId.IsWellFormed(record.tagId))
                    continue;
                yield return new HiddenTagDto
                {
                    tagId = record.tagId.Trim(),
                    hiddenAt = record.hiddenAt,
                    hiddenBy = record.hiddenBy,
                };
            }
        }

        public static void SaveListState(string fullPath, AssetTagsList state)
        {
            var existingHidden = ReadHiddenTagsFromListFile(fullPath);
            SaveListStateInternal(fullPath, state, existingHidden);
        }

        public static Dictionary<string, HiddenTag> LoadHiddenTags(string workstationToken)
        {
            var result = new Dictionary<string, HiddenTag>(StringComparer.OrdinalIgnoreCase);
            var fullPath = GetLocalAssetTagListJsonFullPath(workstationToken);
            var records = ReadHiddenTagsFromListFile(fullPath);
            for (var i = 0; i < records.Count; i++)
                result[records[i].tagId] = records[i];

            return result;
        }

        public static void SaveHiddenTags(string workstationToken, Dictionary<string, HiddenTag> hiddenTags)
        {
            var fullPath = GetLocalAssetTagListJsonFullPath(workstationToken);
            var list = new AssetTagsList();
            TryReadList(fullPath, list);
            SaveListStateInternal(fullPath, list, hiddenTags?.Values);
        }

        static void ImportIfUnderAssets(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return;
            if (IsRunningOnAssetImportWorker)
                return;
            var normalized = Path.GetFullPath(fullPath);
            if (!AssetTagsManager.TryDiskFullPathToAssetPath(normalized, out var assetPath))
                return;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        public static string WorkstationTokenCached;

        public static string ResolveLastModifiedBy()
        {
            var ws = WorkstationTokenCached ?? AssetTagsClientId.GetOrCreateClientId();
            return string.IsNullOrWhiteSpace(ws) ? "unknown" : ws;
        }

    }
}
#endif
