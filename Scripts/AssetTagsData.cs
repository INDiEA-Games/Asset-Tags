#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public sealed class AssetTagsData
    {
        static readonly StringComparer TagComparer = StringComparer.OrdinalIgnoreCase;

        [Serializable]
        public sealed class AssetTagEntry
        {
            public string name;
            public string lastModifiedAtUtc;
            public string lastModifiedBy;

            public static AssetTagEntry Clone(AssetTagEntry e)
            {
                if (e == null)
                    return null;
                return new AssetTagEntry
                {
                    name = string.IsNullOrWhiteSpace(e.name) ? string.Empty : e.name.Trim(),
                    lastModifiedAtUtc = e.lastModifiedAtUtc,
                    lastModifiedBy = e.lastModifiedBy,
                };
            }
        }

        [Serializable]
        public sealed class TagInfo
        {
            public string guid;
            public List<AssetTagEntry> tags = new List<AssetTagEntry>();

            public List<string> GetTagNames()
            {
                if (tags == null || tags.Count == 0)
                    return new List<string>();
                var result = new List<string>(tags.Count);
                foreach (var e in tags)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.name))
                        continue;
                    result.Add(e.name.Trim());
                }

                return result;
            }
        }

        public List<TagInfo> assetTags = new List<TagInfo>();

        static void StampNew(AssetTagEntry e)
        {
            e.lastModifiedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            e.lastModifiedBy = AssetTagsJsonRepository.ResolveLastModifiedBy();
        }

        public void AddTag(string guid, string tag)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrWhiteSpace(tag))
                return;
            var normalized = tag.Trim();
            var row = assetTags.Find(x => x.guid == guid);
            if (row == null)
            {
                row = new TagInfo { guid = guid };
                assetTags.Add(row);
            }

            if (row.tags == null)
                row.tags = new List<AssetTagEntry>();
            var existing = row.tags.Find(x => TagComparer.Equals(x.name, normalized));
            if (existing != null)
                return;
            var entry = new AssetTagEntry { name = normalized };
            StampNew(entry);
            row.tags.Add(entry);
        }

        public void RemoveTag(string guid, string tag)
        {
            if (string.IsNullOrEmpty(guid))
                return;
            var row = assetTags.Find(x => x.guid == guid);
            if (row?.tags == null)
                return;
            row.tags.RemoveAll(x => x != null && TagComparer.Equals(x.name, tag));
            if (row.tags.Count == 0)
                assetTags.Remove(row);
        }

        public List<string> GetTags(string guid)
        {
            var row = assetTags.Find(x => x.guid == guid);
            return row?.GetTagNames() ?? new List<string>();
        }

        public void RenameTag(string oldTag, string newTag)
        {
            if (string.IsNullOrWhiteSpace(oldTag) || string.IsNullOrWhiteSpace(newTag))
                return;
            var normalizedOld = oldTag.Trim();
            var normalizedNew = newTag.Trim();
            if (TagComparer.Equals(normalizedOld, normalizedNew))
                return;

            foreach (var row in assetTags)
            {
                if (row?.tags == null)
                    continue;
                foreach (var e in row.tags)
                {
                    if (e == null || !TagComparer.Equals(e.name, normalizedOld))
                        continue;
                    e.name = normalizedNew;
                    StampNew(e);
                }

                DedupeTagEntries(row);
            }
        }

        void DedupeTagEntries(TagInfo row)
        {
            if (row?.tags == null || row.tags.Count <= 1)
                return;
            var seen = new HashSet<string>(TagComparer);
            var kept = new List<AssetTagEntry>();
            foreach (var e in row.tags)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.name))
                    continue;
                var n = e.name.Trim();
                if (!seen.Add(n))
                    continue;
                kept.Add(e);
            }

            row.tags = kept;
        }

        public void ReplaceOrAddTagEntry(string guid, AssetTagEntry entry)
        {
            if (string.IsNullOrEmpty(guid) || entry == null || string.IsNullOrWhiteSpace(entry.name))
                return;
            var name = entry.name.Trim();
            var row = assetTags.Find(x => x.guid == guid);
            if (row == null)
            {
                row = new TagInfo { guid = guid, tags = new List<AssetTagEntry>() };
                assetTags.Add(row);
            }

            if (row.tags == null)
                row.tags = new List<AssetTagEntry>();
            var existing = row.tags.Find(x => TagComparer.Equals(x.name, name));
            var c = AssetTagEntry.Clone(entry);
            c.name = name;
            if (existing != null)
            {
                existing.name = c.name;
                existing.lastModifiedAtUtc = c.lastModifiedAtUtc;
                existing.lastModifiedBy = c.lastModifiedBy;
            }
            else
            {
                row.tags.Add(c);
            }
        }

        public void ReorderAllTagsByGlobalOrder(List<string> globalOrder)
        {
            var rank = new Dictionary<string, int>(TagComparer);
            for (var i = 0; i < globalOrder.Count; i++)
            {
                if (!rank.ContainsKey(globalOrder[i]))
                    rank.Add(globalOrder[i], i);
            }

            foreach (var row in assetTags)
            {
                if (row?.tags == null || row.tags.Count == 0)
                    continue;
                row.tags = row.tags
                    .Where(e => e != null && !string.IsNullOrWhiteSpace(e.name))
                    .Select((e, index) => new
                    {
                        e,
                        index,
                        order = rank.TryGetValue(e.name.Trim(), out var value) ? value : int.MaxValue,
                    })
                    .OrderBy(x => x.order)
                    .ThenBy(x => x.index)
                    .Select(x => x.e)
                    .ToList();
            }
        }
    }
}
#endif
