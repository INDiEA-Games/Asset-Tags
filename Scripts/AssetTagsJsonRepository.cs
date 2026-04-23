#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public static class AssetTagsJsonRepository
    {
        public const string LegacyDataAssetPath = "Assets/INDiEA/Asset Tags/AssetTagsData.asset";
        public const string LegacyListAssetPath = "Assets/INDiEA/Asset Tags/AssetTagsList.asset";

        const string GlobalAssetTagsFileName = "AssetTagsData.json";
        const string GlobalAssetTagListFileName = "AssetTagsList.json";

        const string LocalDataFolderUnderRoot = "Data";
        const string LocalDataAssetTagsPrefix = "AssetTagsData_";
        const string LocalDataAssetListPrefix = "AssetTagsList_";

        static string ProjectRootFull =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static string UnityLibraryFull =>
            Path.GetFullPath(Path.Combine(ProjectRootFull, "Library"));

        static string GlobalDataDirFull =>
            Path.Combine(UnityLibraryFull, "INDiEA", "Asset Tags", "Data");

        static string LegacyGlobalDataDirOutsideLibraryFull =>
            Path.Combine(ProjectRootFull, "INDiEA", "Asset Tags", "Data");

        static void TryMigrateGlobalJsonFromLegacyProjectRootIndieaFolder()
        {
            try
            {
                var oldDir = LegacyGlobalDataDirOutsideLibraryFull;
                if (!Directory.Exists(oldDir))
                    return;
                Directory.CreateDirectory(GlobalDataDirFull);
                foreach (var fileName in new[] { GlobalAssetTagsFileName, GlobalAssetTagListFileName })
                {
                    var oldPath = Path.Combine(oldDir, fileName);
                    var newPath = Path.Combine(GlobalDataDirFull, fileName);
                    if (!File.Exists(oldPath) || File.Exists(newPath))
                        continue;
                    File.Copy(oldPath, newPath, false);
                }
            }
            catch{}
        }

        public static string LocalDataFolderAssetPath =>
            $"{AssetTagsManager.RootFolderPath}/{LocalDataFolderUnderRoot}";

        static string LocalDataFolderFull =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "INDiEA", "Asset Tags", LocalDataFolderUnderRoot));

        public static string GetLocalAssetTagsJsonFullPath(string workstationToken) =>
            Path.Combine(LocalDataFolderFull, $"{LocalDataAssetTagsPrefix}{workstationToken}.json");

        public static string GetLocalAssetTagListJsonFullPath(string workstationToken) =>
            Path.Combine(LocalDataFolderFull, $"{LocalDataAssetListPrefix}{workstationToken}.json");

        static void TryRenameLocalJsonFromLegacyLongSuffix(string shortToken)
        {
            if (string.IsNullOrEmpty(shortToken) || shortToken.Length != AssetTagsClientId.ShortIdLength)
                return;
            if (!Directory.Exists(LocalDataFolderFull))
                return;
            TryRenameOneSeries(LocalDataAssetTagsPrefix, shortToken);
            TryRenameOneSeries(LocalDataAssetListPrefix, shortToken);
        }

        static void TryRenameOneSeries(string prefix, string shortToken)
        {
            foreach (var fullPath in Directory.GetFiles(LocalDataFolderFull, prefix + "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(fullPath);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var suffix = name.Substring(prefix.Length);
                if (suffix.Length != 32 || !Is32Hex(suffix))
                    continue;
                if (!suffix.StartsWith(shortToken, StringComparison.OrdinalIgnoreCase))
                    continue;
                var dest = Path.Combine(LocalDataFolderFull, prefix + shortToken + ".json");
                if (string.Equals(fullPath, dest, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(dest))
                    continue;
                File.Move(fullPath, dest);
                ImportIfUnderAssets(dest);
            }
        }

        static bool Is32Hex(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 32)
                return false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')
                    continue;
                return false;
            }

            return true;
        }

        static string GlobalAssetTagsFullPath =>
            Path.Combine(GlobalDataDirFull, GlobalAssetTagsFileName);

        static string GlobalAssetTagListFullPath =>
            Path.Combine(GlobalDataDirFull, GlobalAssetTagListFileName);

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
            public string name;
            public string lastModifiedAtUtc;
            public string lastModifiedBy;
        }

        [Serializable]
        class LegacyStringTagsDataFileDto
        {
            public List<LegacyStringTagsRowDto> assetTags = new List<LegacyStringTagsRowDto>();
        }

        [Serializable]
        class LegacyStringTagsRowDto
        {
            public string guid;
            public List<string> tags = new List<string>();
        }

        [Serializable]
        class ListFileDto
        {
            public List<ListEntryDto> tags = new List<ListEntryDto>();
        }

        [Serializable]
        class ListEntryDto
        {
            public string tagName;
            public float colorR;
            public float colorG;
            public float colorB;
            public float colorA = 1f;
            public string lastModifiedAtUtc;
            public string lastModifiedBy;
        }

        public static void EnsureInfrastructure()
        {
            TryMigrateGlobalJsonFromLegacyProjectRootIndieaFolder();
            Directory.CreateDirectory(GlobalDataDirFull);
            WriteTextIfMissing(GlobalAssetTagsFullPath, JsonUtility.ToJson(new DataFileDto(), true));
            WriteTextIfMissing(GlobalAssetTagListFullPath, JsonUtility.ToJson(new ListFileDto(), true));

            EnsureRootFolderExists();
            if (!AssetDatabase.IsValidFolder(LocalDataFolderAssetPath))
                AssetDatabase.CreateFolder(AssetTagsManager.RootFolderPath, LocalDataFolderUnderRoot);

            var token = AssetTagsClientId.GetOrCreateClientId();
            WorkstationTokenCached = token;
            TryRenameLocalJsonFromLegacyLongSuffix(token);
            var dataPath = GetLocalAssetTagsJsonFullPath(token);
            var listPath = GetLocalAssetTagListJsonFullPath(token);

            var localDataExists = File.Exists(dataPath);
            var localListExists = File.Exists(listPath);
            if (localDataExists && localListExists)
            {
                DeleteLegacyScriptableObjectsIfPresent();
                return;
            }

            var legacyDataFull = LegacySoYamlImport.ToFullPathFromAssets(LegacyDataAssetPath);
            var legacyListFull = LegacySoYamlImport.ToFullPathFromAssets(LegacyListAssetPath);

            var data = new AssetTagsData();
            var list = new AssetTagsList();

            if (localDataExists)
                TryReadData(dataPath, data);
            else if (legacyDataFull != null && File.Exists(legacyDataFull))
                LegacySoYamlImport.TryImportAssetTagsDataYaml(legacyDataFull, data);

            if (localListExists)
                TryReadList(listPath, list);
            else if (legacyListFull != null && File.Exists(legacyListFull))
                LegacySoYamlImport.TryImportAssetTagsListYaml(legacyListFull, list);

            SaveDataState(dataPath, data);
            SaveListState(listPath, list);

            DeleteLegacyScriptableObjectsIfPresent();
        }

        static void DeleteLegacyScriptableObjectsIfPresent()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LegacyDataAssetPath) != null)
                AssetDatabase.DeleteAsset(LegacyDataAssetPath);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LegacyListAssetPath) != null)
                AssetDatabase.DeleteAsset(LegacyListAssetPath);
            AssetDatabase.SaveAssets();
        }

        static void EnsureRootFolderExists()
        {
            if (AssetDatabase.IsValidFolder(AssetTagsManager.RootFolderPath))
                return;

            const string indieaFolder = "Assets/INDiEA";
            if (!AssetDatabase.IsValidFolder(indieaFolder))
                AssetDatabase.CreateFolder("Assets", "INDiEA");
            AssetDatabase.CreateFolder(indieaFolder, "Asset Tags");
        }

        static void WriteTextIfMissing(string fullPath, string contents)
        {
            if (File.Exists(fullPath))
                return;
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, contents);
        }

        public static void LoadLocal(string workstationToken, AssetTagsData data, AssetTagsList list)
        {
            data.assetTags.Clear();
            list.tags.Clear();

            var dataPath = GetLocalAssetTagsJsonFullPath(workstationToken);
            var listPath = GetLocalAssetTagListJsonFullPath(workstationToken);

            TryReadData(dataPath, data);
            TryReadList(listPath, list);
        }

        public static void LoadGlobal(AssetTagsData data, AssetTagsList list)
        {
            data.assetTags.Clear();
            list.tags.Clear();

            TryReadData(GlobalAssetTagsFullPath, data);
            TryReadList(GlobalAssetTagListFullPath, list);
        }

        /// <summary>
        /// Merges every local <c>AssetTagsData_*.json</c> / <c>AssetTagsList_*.json</c> (any client suffix),
        /// reconciles with the Library global cache using <c>lastModifiedAtUtc</c>, persists the result to
        /// global JSON, and returns that merged snapshot for editor state.
        /// </summary>
        public static void RebuildMergedFromAllLocalFilesAndGlobalCache(
            out AssetTagsData mergedData,
            out AssetTagsList mergedList)
        {
            mergedData = new AssetTagsData();
            mergedList = new AssetTagsList();

            if (Directory.Exists(LocalDataFolderFull))
            {
                foreach (var fullPath in Directory.GetFiles(LocalDataFolderFull, LocalDataAssetTagsPrefix + "*.json")
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    var chunk = new AssetTagsData();
                    TryReadData(fullPath, chunk);
                    PreferNewerMergeDataInto(mergedData, chunk);
                }

                foreach (var fullPath in Directory.GetFiles(LocalDataFolderFull, LocalDataAssetListPrefix + "*.json")
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    var chunk = new AssetTagsList();
                    TryReadList(fullPath, chunk);
                    PreferNewerMergeListInto(mergedList, chunk);
                }
            }

            var globalData = new AssetTagsData();
            var globalList = new AssetTagsList();
            LoadGlobal(globalData, globalList);
            PreferNewerMergeDataInto(mergedData, globalData);
            PreferNewerMergeListInto(mergedList, globalList);

            SaveDataState(GlobalAssetTagsFullPath, mergedData);
            SaveListState(GlobalAssetTagListFullPath, mergedList);
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

        static bool IsUtcStrictlyNewerThan(string incomingUtc, string existingUtc)
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

        static void PreferNewerMergeDataInto(AssetTagsData target, AssetTagsData incoming)
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
                    if (e == null || string.IsNullOrWhiteSpace(e.name))
                        continue;
                    var name = e.name.Trim();
                    var trow = target.assetTags.Find(x =>
                        x != null && string.Equals(x.guid, guid, StringComparison.OrdinalIgnoreCase));
                    AssetTagsData.AssetTagEntry existingEntry = null;
                    if (trow?.tags != null)
                        existingEntry = trow.tags.Find(x =>
                            x != null && string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));
                    if (existingEntry == null)
                        target.ReplaceOrAddTagEntry(guid, AssetTagsData.AssetTagEntry.Clone(e));
                    else if (IsUtcStrictlyNewerThan(e.lastModifiedAtUtc, existingEntry.lastModifiedAtUtc))
                        target.ReplaceOrAddTagEntry(guid, AssetTagsData.AssetTagEntry.Clone(e));
                }
            }
        }

        static void PreferNewerMergeListInto(AssetTagsList target, AssetTagsList incoming)
        {
            if (incoming?.tags == null)
                return;
            foreach (var e in incoming.tags)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.tagName))
                    continue;
                var name = e.tagName.Trim();
                var existing = target.tags.Find(x =>
                    x != null && string.Equals(x.tagName, name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                    target.ReplaceOrAddListEntry(CloneListTagInfo(e));
                else if (IsUtcStrictlyNewerThan(e.lastModifiedAtUtc, existing.lastModifiedAtUtc))
                    target.ReplaceOrAddListEntry(CloneListTagInfo(e));
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
                        if (e == null || string.IsNullOrWhiteSpace(e.name))
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
                    if (e == null || string.IsNullOrWhiteSpace(e.name))
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
                lastModifiedAtUtc = e.lastModifiedAtUtc,
                lastModifiedBy = e.lastModifiedBy,
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
                        x != null && string.Equals(x.tagName, n, StringComparison.OrdinalIgnoreCase));
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

        static bool DataJsonLooksLikeLegacyStringTagArrays(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            var idx = 0;
            while ((idx = text.IndexOf("\"tags\"", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var bracket = text.IndexOf('[', idx);
                if (bracket < 0)
                    return false;
                var i = bracket + 1;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
                if (i >= text.Length)
                    return false;
                var c = text[i];
                if (c == '"')
                    return true;
                if (c == '{')
                    return false;
                if (c == ']')
                {
                    idx = bracket + 1;
                    continue;
                }

                idx = bracket + 1;
            }

            return false;
        }

        static void TryReadDataLegacyStringTags(string text, AssetTagsData target)
        {
            var dto = JsonUtility.FromJson<LegacyStringTagsDataFileDto>(text);
            if (dto?.assetTags == null)
                return;
            foreach (var row in dto.assetTags)
            {
                if (row == null || string.IsNullOrEmpty(row.guid) || row.tags == null)
                    continue;
                foreach (var t in row.tags)
                {
                    if (string.IsNullOrWhiteSpace(t))
                        continue;
                    target.ReplaceOrAddTagEntry(
                        row.guid.Trim(),
                        new AssetTagsData.AssetTagEntry { name = t.Trim() });
                }
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
                    if (t == null || string.IsNullOrWhiteSpace(t.name))
                        continue;
                    target.ReplaceOrAddTagEntry(
                        row.guid.Trim(),
                        new AssetTagsData.AssetTagEntry
                        {
                            name = t.name.Trim(),
                            lastModifiedAtUtc = t.lastModifiedAtUtc,
                            lastModifiedBy = t.lastModifiedBy,
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
                if (DataJsonLooksLikeLegacyStringTagArrays(text))
                    TryReadDataLegacyStringTags(text, target);
                else
                    TryReadDataWithPerTagMeta(text, target);
            }
            catch{}
            {}
        }

        static void TryReadList(string fullPath, AssetTagsList target)
        {
            if (!File.Exists(fullPath))
                return;
            try
            {
                var dto = JsonUtility.FromJson<ListFileDto>(File.ReadAllText(fullPath));
                if (dto?.tags == null)
                    return;
                foreach (var e in dto.tags)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.tagName))
                        continue;
                    var name = e.tagName.Trim();
                    var ti = new AssetTagsList.TagInfo(name, new Color(e.colorR, e.colorG, e.colorB, e.colorA))
                    {
                        lastModifiedAtUtc = e.lastModifiedAtUtc,
                        lastModifiedBy = e.lastModifiedBy,
                    };
                    target.ReplaceOrAddListEntry(ti);
                }
            }
            catch{}
        }

        public static void SaveDataState(string fullPath, AssetTagsData state)
        {
            var dto = new DataFileDto();
            foreach (var row in state.assetTags)
            {
                if (row == null || string.IsNullOrEmpty(row.guid) || row.tags == null)
                    continue;
                var rowDto = new DataRowDto { guid = row.guid.Trim() };
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in row.tags)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.name))
                        continue;
                    var nm = t.name.Trim();
                    if (!seenNames.Add(nm))
                        continue;
                    rowDto.tags.Add(new DataTagDto
                    {
                        name = nm,
                        lastModifiedAtUtc = t.lastModifiedAtUtc,
                        lastModifiedBy = t.lastModifiedBy,
                    });
                }

                if (rowDto.tags.Count == 0)
                    continue;
                dto.assetTags.Add(rowDto);
            }

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, JsonUtility.ToJson(dto, true));
            ImportIfUnderAssets(fullPath);
        }

        public static void SaveListState(string fullPath, AssetTagsList state)
        {
            var dto = new ListFileDto();
            foreach (var e in state.tags)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.tagName))
                    continue;
                dto.tags.Add(new ListEntryDto
                {
                    tagName = e.tagName.Trim(),
                    colorR = e.color.r,
                    colorG = e.color.g,
                    colorB = e.color.b,
                    colorA = e.color.a,
                    lastModifiedAtUtc = e.lastModifiedAtUtc,
                    lastModifiedBy = e.lastModifiedBy,
                });
            }

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, JsonUtility.ToJson(dto, true));
            ImportIfUnderAssets(fullPath);
        }

        static void ImportIfUnderAssets(string fullPath)
        {
            if (!fullPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                return;
            var rel = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace(Path.DirectorySeparatorChar, '/');
            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
        }

        public static string WorkstationTokenCached;

        /// <summary>
        /// Non-identifying editor stamp for merge metadata. Intentionally excludes OS account names.
        /// </summary>
        public static string ResolveLastModifiedBy()
        {
            var ws = WorkstationTokenCached ?? AssetTagsClientId.GetOrCreateClientId();
            return string.IsNullOrWhiteSpace(ws) ? "unknown" : ws;
        }

        static class LegacySoYamlImport
        {
            static readonly Regex GuidRow = new Regex(@"^\s*-\s*guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Compiled);
            static readonly Regex ColorInLine = new Regex(
                @"\{r:\s*([0-9.eE+-]+)\s*,\s*g:\s*([0-9.eE+-]+)\s*,\s*b:\s*([0-9.eE+-]+)\s*,\s*a:\s*([0-9.eE+-]+)\s*\}",
                RegexOptions.Compiled);

            public static string ToFullPathFromAssets(string assetPath)
            {
                if (string.IsNullOrEmpty(assetPath))
                    return null;
                var n = assetPath.Replace('\\', '/');
                if (!n.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    return null;
                var tail = n.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(Application.dataPath, tail));
            }

            public static bool TryImportAssetTagsDataYaml(string fullPath, AssetTagsData state)
            {
                if (state == null || string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                    return false;

                var lines = File.ReadAllLines(fullPath);
                var headerIdx = IndexOfLineEndingWith(lines, "assetTags:");
                if (headerIdx < 0)
                    return false;
                if (lines[headerIdx].Replace(" ", string.Empty).IndexOf("assetTags:[]", StringComparison.Ordinal) >= 0)
                    return false;

                var i = headerIdx + 1;
                var added = false;
                while (i < lines.Length)
                {
                    var line = lines[i];
                    if (IsTwoSpaceMonoField(line))
                        break;

                    var gm = GuidRow.Match(line);
                    if (!gm.Success)
                    {
                        i++;
                        continue;
                    }

                    var guid = gm.Groups[1].Value.ToLowerInvariant();
                    i++;
                    while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                        i++;
                    if (i >= lines.Length)
                        break;
                    if (!lines[i].Trim().StartsWith("tags:", StringComparison.Ordinal))
                    {
                        i++;
                        continue;
                    }

                    i++;
                    while (i < lines.Length)
                    {
                        var tLine = lines[i];
                        if (GuidRow.IsMatch(tLine))
                            break;
                        if (IsTwoSpaceMonoField(tLine))
                            return added;

                        var tt = tLine.Trim();
                        if (tt.StartsWith("- ", StringComparison.Ordinal) && !tt.StartsWith("- guid:", StringComparison.Ordinal))
                        {
                            var tag = UnquoteYamlScalar(tt.Substring(2).Trim());
                            if (!string.IsNullOrWhiteSpace(tag))
                            {
                                state.AddTag(guid, tag.Trim());
                                added = true;
                            }

                            i++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(tt))
                        {
                            i++;
                            continue;
                        }

                        i++;
                        break;
                    }
                }

                return added;
            }

            public static bool TryImportAssetTagsListYaml(string fullPath, AssetTagsList state)
            {
                if (state == null || string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                    return false;

                var lines = File.ReadAllLines(fullPath);
                var headerIdx = IndexOfListOnlyTagsHeaderLine(lines);
                if (headerIdx < 0)
                    return false;
                if (lines[headerIdx].Replace(" ", string.Empty).IndexOf("tags:[]", StringComparison.Ordinal) >= 0)
                    return false;

                var i = headerIdx + 1;
                var added = false;
                while (i < lines.Length)
                {
                    var line = lines[i];
                    if (IsTwoSpaceMonoField(line))
                        break;

                    var trim = line.Trim();
                    if (trim.StartsWith("- tagName:", StringComparison.Ordinal))
                    {
                        var name = UnquoteYamlScalar(trim.Substring("- tagName:".Length).Trim());
                        Color? color = null;
                        i++;
                        if (i < lines.Length)
                        {
                            var cm = ColorInLine.Match(lines[i]);
                            if (cm.Success
                                && float.TryParse(cm.Groups[1].Value, out var r)
                                && float.TryParse(cm.Groups[2].Value, out var g)
                                && float.TryParse(cm.Groups[3].Value, out var b)
                                && float.TryParse(cm.Groups[4].Value, out var a))
                            {
                                color = new Color(r, g, b, a);
                                i++;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            var n = name.Trim();
                            state.AddTag(n);
                            if (color.HasValue)
                                state.SetTagColor(n, color.Value);
                            added = true;
                        }

                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(trim))
                    {
                        i++;
                        continue;
                    }

                    if (trim.StartsWith("- ", StringComparison.Ordinal))
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                return added;
            }

            static int IndexOfLineEndingWith(string[] lines, string suffix)
            {
                for (var j = 0; j < lines.Length; j++)
                {
                    if (lines[j].TrimEnd().EndsWith(suffix, StringComparison.Ordinal))
                        return j;
                }

                return -1;
            }

            static int IndexOfListOnlyTagsHeaderLine(string[] lines)
            {
                for (var j = 0; j < lines.Length; j++)
                {
                    var t = lines[j].TrimEnd();
                    if (!t.EndsWith("tags:", StringComparison.Ordinal))
                        continue;
                    if (t.IndexOf("assetTags", StringComparison.Ordinal) >= 0)
                        continue;
                    return j;
                }

                return -1;
            }

            static bool IsTwoSpaceMonoField(string line)
            {
                if (string.IsNullOrEmpty(line) || line.Length < 3)
                    return false;
                if (line[0] != ' ' || line[1] != ' ')
                    return false;
                return line[2] == 'm' && line.Length > 3 && line[3] == '_';
            }

            static string UnquoteYamlScalar(string s)
            {
                if (string.IsNullOrEmpty(s))
                    return s;
                s = s.Trim();
                if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                    return s.Substring(1, s.Length - 2).Replace("''", "'");
                if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                    return s.Substring(1, s.Length - 2).Replace("\\\"", "\"");
                return s;
            }
        }
    }
}
#endif
