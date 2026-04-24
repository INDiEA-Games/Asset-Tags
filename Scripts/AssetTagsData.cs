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
            public string tagId;

            public string linkUpdatedAt;
            public string linkUpdatedBy;

            public static AssetTagEntry Clone(AssetTagEntry e)
            {
                if (e == null)
                    return null;
                return new AssetTagEntry
                {
                    tagId = string.IsNullOrWhiteSpace(e.tagId) ? null : e.tagId.Trim(),
                    linkUpdatedAt = e.linkUpdatedAt,
                    linkUpdatedBy = e.linkUpdatedBy,
                };
            }
        }

        [Serializable]
        public sealed class TagInfo
        {
            public string guid;
            public List<AssetTagEntry> tags = new List<AssetTagEntry>();

            public List<string> GetResolvedTagNames(AssetTagsList tagList)
            {
                if (tags == null || tags.Count == 0 || tagList == null)
                    return new List<string>();
                var result = new List<string>(tags.Count);
                var seen = new HashSet<string>(TagComparer);
                foreach (var e in tags)
                {
                    if (e == null || !AssetTagsTagId.IsWellFormed(e.tagId))
                        continue;
                    if (!tagList.TryGetName(e.tagId, out var name) || string.IsNullOrWhiteSpace(name))
                        continue;
                    name = name.Trim();
                    if (!seen.Add(name))
                        continue;
                    result.Add(name);
                }

                return result;
            }
        }

        public List<TagInfo> assetTags = new List<TagInfo>();

        static void StampLink(AssetTagEntry e)
        {
            e.linkUpdatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            e.linkUpdatedBy = AssetTagsJsonRepository.ResolveLastModifiedBy();
        }

        public void AddTagId(string guid, string tagIdRaw)
        {
            if (string.IsNullOrEmpty(guid) || !AssetTagsTagId.IsWellFormed(tagIdRaw))
                return;
            var tagId = tagIdRaw.Trim();
            var row = assetTags.Find(x => x.guid == guid);
            if (row == null)
            {
                row = new TagInfo { guid = guid };
                assetTags.Add(row);
            }

            if (row.tags == null)
                row.tags = new List<AssetTagEntry>();
            if (row.tags.Exists(x =>
                    x != null
                    && AssetTagsTagId.IsWellFormed(x.tagId)
                    && string.Equals(x.tagId, tagId, StringComparison.OrdinalIgnoreCase)))
                return;
            var entry = new AssetTagEntry { tagId = tagId };
            StampLink(entry);
            row.tags.Add(entry);
        }

        public void AddTag(string guid, string tagDisplayName, AssetTagsList tagList)
        {
            if (tagList == null || string.IsNullOrWhiteSpace(tagDisplayName))
                return;
            if (!tagList.TryGetId(tagDisplayName.Trim(), out var tagId))
                return;
            AddTagId(guid, tagId);
        }

        public void RemoveTagId(string guid, string tagIdRaw)
        {
            if (string.IsNullOrEmpty(guid) || !AssetTagsTagId.IsWellFormed(tagIdRaw))
                return;
            var tagId = tagIdRaw.Trim();
            var row = assetTags.Find(x => x.guid == guid);
            if (row?.tags == null)
                return;
            row.tags.RemoveAll(x =>
                x != null
                && AssetTagsTagId.IsWellFormed(x.tagId)
                && string.Equals(x.tagId, tagId, StringComparison.OrdinalIgnoreCase));
            if (row.tags.Count == 0)
                assetTags.Remove(row);
        }

        public void RemoveTag(string guid, string tagDisplayName, AssetTagsList tagList)
        {
            if (tagList == null || string.IsNullOrWhiteSpace(tagDisplayName))
                return;
            if (!tagList.TryGetId(tagDisplayName.Trim(), out var tagId))
                return;
            RemoveTagId(guid, tagId);
        }

        public List<string> GetTags(string guid, AssetTagsList tagList)
        {
            var row = assetTags.Find(x => x.guid == guid);
            return row?.GetResolvedTagNames(tagList) ?? new List<string>();
        }

        public void ReplaceOrAddTagEntry(string guid, AssetTagEntry entry)
        {
            if (string.IsNullOrEmpty(guid) || entry == null || !AssetTagsTagId.IsWellFormed(entry.tagId))
                return;
            var tagId = entry.tagId.Trim();
            var row = assetTags.Find(x => x.guid == guid);
            if (row == null)
            {
                row = new TagInfo { guid = guid, tags = new List<AssetTagEntry>() };
                assetTags.Add(row);
            }

            if (row.tags == null)
                row.tags = new List<AssetTagEntry>();
            var existing = row.tags.Find(x =>
                x != null
                && AssetTagsTagId.IsWellFormed(x.tagId)
                && string.Equals(x.tagId, tagId, StringComparison.OrdinalIgnoreCase));
            var c = AssetTagEntry.Clone(entry);
            c.tagId = tagId;
            if (existing != null)
            {
                existing.tagId = c.tagId;
                existing.linkUpdatedAt = c.linkUpdatedAt;
                existing.linkUpdatedBy = c.linkUpdatedBy;
            }
            else
            {
                row.tags.Add(c);
            }
        }

        public void ReorderAllTagsByGlobalOrder(AssetTagsList tagList, List<string> globalOrder)
        {
            if (tagList == null || globalOrder == null || globalOrder.Count == 0)
                return;
            var rank = new Dictionary<string, int>(TagComparer);
            for (var i = 0; i < globalOrder.Count; i++)
            {
                var key = globalOrder[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                var k = key.Trim();
                if (!rank.ContainsKey(k))
                    rank.Add(k, i);
            }

            foreach (var row in assetTags)
            {
                if (row?.tags == null || row.tags.Count == 0)
                    continue;
                row.tags = row.tags
                    .Where(e => e != null && AssetTagsTagId.IsWellFormed(e.tagId))
                    .Select((e, index) => new
                    {
                        e,
                        index,
                        display = tagList.TryGetName(e.tagId, out var displayName) ? displayName.Trim() : string.Empty,
                    })
                    .Select(x => new
                    {
                        x.e,
                        x.index,
                        order = string.IsNullOrEmpty(x.display) || !rank.TryGetValue(x.display, out var value)
                            ? int.MaxValue
                            : value,
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
