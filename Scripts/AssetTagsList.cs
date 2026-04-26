#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public static class AssetTagsTagId
    {
        public static string NewTagId() => Guid.NewGuid().ToString("N");

        public static bool IsWellFormed(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var s = value.Trim();
            if (s.Length != 32)
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
    }

    public sealed class AssetTagsList
    {
        static readonly StringComparer TagComparer = StringComparer.OrdinalIgnoreCase;

        [Serializable]
        public sealed class TagInfo
        {
            public string tagId;

            public string tagName;
            public Color color;

            public string tagUpdatedAt;
            public string tagUpdatedBy;

            public string orderKey;

            public string orderUpdatedAt;
            public string orderUpdatedBy;

            public int order;

            public TagInfo(string name, Color col)
            {
                tagName = name;
                color = col;
            }

            internal void StampTag()
            {
                tagUpdatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                tagUpdatedBy = AssetTagsJsonRepository.ResolveLastModifiedBy();
            }

            public void StampOrderUpdate()
            {
                orderUpdatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                orderUpdatedBy = AssetTagsJsonRepository.ResolveLastModifiedBy();
            }
        }

        public List<TagInfo> tags = new List<TagInfo>();

        public void FillMissingIds()
        {
            if (tags == null)
                return;
            for (var i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                if (t == null || string.IsNullOrWhiteSpace(t.tagName))
                    continue;
                if (!AssetTagsTagId.IsWellFormed(t.tagId))
                    t.tagId = AssetTagsTagId.NewTagId();
            }
        }

        public bool TryGetId(string tag, out string tagId)
        {
            tagId = null;
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            var row = tags.Find(x => MatchesTagName(x, tag));
            if (row == null || !AssetTagsTagId.IsWellFormed(row.tagId))
                return false;
            tagId = row.tagId;
            return true;
        }

        public bool TryFind(string tagId, out TagInfo row)
        {
            row = null;
            if (string.IsNullOrWhiteSpace(tagId) || !AssetTagsTagId.IsWellFormed(tagId))
                return false;
            var id = tagId.Trim();
            row = tags.Find(x =>
                x != null
                && AssetTagsTagId.IsWellFormed(x.tagId)
                && string.Equals(x.tagId, id, StringComparison.OrdinalIgnoreCase));
            return row != null;
        }

        public bool TryGetName(string tagId, out string tagName)
        {
            tagName = null;
            if (!TryFind(tagId, out var row) || string.IsNullOrWhiteSpace(row.tagName))
                return false;
            tagName = row.tagName.Trim();
            return true;
        }

        static bool MatchesTagName(TagInfo x, string tag)
        {
            if (x == null || string.IsNullOrWhiteSpace(tag))
                return false;
            if (string.IsNullOrWhiteSpace(x.tagName))
                return false;
            return TagComparer.Equals(x.tagName.Trim(), tag.Trim());
        }

        public List<string> GetAvailableTags()
        {
            AssetTagsManager.TagSortOrder.SortTagsListInPlace(this);
            var result = new List<string>(tags.Count);
            foreach (var entry in tags)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.tagName))
                    continue;
                result.Add(entry.tagName.Trim());
            }

            return result;
        }

        public void AddTag(string tag) => TryAddTagIfMissing(tag);

        public bool TryAddTagIfMissing(string tag, IEnumerable<Color> colorsToAvoid = null)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            var name = tag.Trim();
            if (HasTag(name))
                return false;
            var row = new TagInfo(name, AssetTagsColorUtilities.GenerateTagColor(GetColorsForNewTag(colorsToAvoid)));
            if (!AssetTagsTagId.IsWellFormed(row.tagId))
                row.tagId = AssetTagsTagId.NewTagId();
            row.StampTag();
            tags.Add(row);
            AssetTagsManager.TagSortOrder.StampNewTagAtEnd(this, row);
            return true;
        }

        IEnumerable<Color> GetColorsForNewTag(IEnumerable<Color> extraColors)
        {
            if (tags != null)
            {
                for (var i = 0; i < tags.Count; i++)
                {
                    var tag = tags[i];
                    if (tag == null || string.IsNullOrWhiteSpace(tag.tagName))
                        continue;
                    yield return tag.color;
                }
            }

            if (extraColors == null)
                yield break;
            foreach (var color in extraColors)
                yield return color;
        }

        public void RemoveTag(string tag) =>
            tags.RemoveAll(x => MatchesTagName(x, tag));

        public void RenameTag(string oldTag, string newTag)
        {
            if (TagComparer.Equals(oldTag, newTag) || HasTag(newTag))
                return;
            var row = tags.Find(x => MatchesTagName(x, oldTag));
            if (row != null)
            {
                row.tagName = newTag.Trim();
                row.StampTag();
            }
        }

        public void MoveTagUp(string tag)
        {
            var i = tags.FindIndex(x => MatchesTagName(x, tag));
            if (i <= 0)
                return;
            (tags[i], tags[i - 1]) = (tags[i - 1], tags[i]);
            AssetTagsManager.TagSortOrder.RepairKeysAfterAdjacentSwap(this, i - 1, i);
        }

        public void MoveTagDown(string tag)
        {
            var i = tags.FindIndex(x => MatchesTagName(x, tag));
            if (i < 0 || i >= tags.Count - 1)
                return;
            (tags[i], tags[i + 1]) = (tags[i + 1], tags[i]);
            AssetTagsManager.TagSortOrder.RepairKeysAfterAdjacentSwap(this, i, i + 1);
        }

        public void ReorderByNames(IReadOnlyList<string> orderedTagNames)
        {
            if (orderedTagNames == null || orderedTagNames.Count == 0)
                return;

            var previousOrder = new List<TagInfo>(tags);
            var byName = new Dictionary<string, TagInfo>(TagComparer);
            foreach (var entry in tags)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.tagName))
                    continue;
                var key = entry.tagName.Trim();
                if (!byName.ContainsKey(key))
                    byName[key] = entry;
            }

            var next = new List<TagInfo>();
            var seen = new HashSet<string>(TagComparer);
            for (var i = 0; i < orderedTagNames.Count; i++)
            {
                var raw = orderedTagNames[i];
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var name = raw.Trim();
                if (!byName.TryGetValue(name, out var info))
                    continue;
                if (!seen.Add(name))
                    continue;
                next.Add(info);
            }

            foreach (var entry in tags)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.tagName))
                    continue;
                var nm = entry.tagName.Trim();
                if (seen.Contains(nm))
                    continue;
                next.Add(entry);
            }

            tags = next;
            UpdateOrderMetadataForChangedRange(previousOrder);
        }

        public void MoveTagBetweenNames(string tag, string previousTag, string nextTag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return;

            AssetTagsManager.TagSortOrder.SortTagsListInPlace(this);

            var row = tags.Find(x => MatchesTagName(x, tag));
            if (row == null)
                return;

            tags.Remove(row);

            var insertIndex = tags.Count;
            if (!string.IsNullOrWhiteSpace(previousTag))
            {
                var previousIndex = tags.FindIndex(x => MatchesTagName(x, previousTag));
                if (previousIndex >= 0)
                    insertIndex = previousIndex + 1;
            }
            else if (!string.IsNullOrWhiteSpace(nextTag))
            {
                var nextIndex = tags.FindIndex(x => MatchesTagName(x, nextTag));
                if (nextIndex >= 0)
                    insertIndex = nextIndex;
            }

            insertIndex = Mathf.Clamp(insertIndex, 0, tags.Count);
            tags.Insert(insertIndex, row);
            UpdateOrderIndicesFromPhysicalOrder();
            if (!TryAssignOrderKeyBetweenNeighbors(insertIndex, row))
                RedistributeAllTagOrderKeysFromPhysicalOrder();
        }

        public bool MoveTagBetweenOrderKeys(string tag, string previousOrderKey, string nextOrderKey, int orderIndex)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            var row = tags.Find(x => MatchesTagName(x, tag));
            if (row == null)
                return false;

            if (!TryAssignOrderKeyBetween(previousOrderKey, nextOrderKey, row))
                return false;

            row.order = Mathf.Max(0, orderIndex);
            return true;
        }

        static bool TryAssignOrderKeyBetween(string previousOrderKey, string nextOrderKey, TagInfo row)
        {
            if (row == null)
                return false;

            var hasLower = AssetTagsManager.TagSortOrder.TryParseOrderKey(previousOrderKey, out var lower);
            var hasUpper = AssetTagsManager.TagSortOrder.TryParseOrderKey(nextOrderKey, out var upper);

            ulong nextKey;
            if (hasLower && hasUpper)
            {
                if (upper <= lower || upper - lower <= 1UL)
                    return false;
                nextKey = lower + (upper - lower) / 2UL;
            }
            else if (hasLower)
            {
                if (lower > ulong.MaxValue - AssetTagsManager.TagSortOrder.KeyStep)
                    return false;
                nextKey = lower + AssetTagsManager.TagSortOrder.KeyStep;
            }
            else if (hasUpper)
            {
                if (upper <= 1UL)
                    return false;
                nextKey = upper / 2UL;
            }
            else
            {
                nextKey = 0x1000UL;
            }

            row.orderKey = AssetTagsManager.TagSortOrder.FormatOrderKey(nextKey);
            row.StampOrderUpdate();
            return true;
        }

        bool TryAssignOrderKeyBetweenNeighbors(int rowIndex, TagInfo row)
        {
            if (row == null || rowIndex < 0 || rowIndex >= tags.Count)
                return false;

            if (tags.Count == 1)
            {
                row.orderKey = AssetTagsManager.TagSortOrder.FormatOrderKey(0x1000UL);
                row.StampOrderUpdate();
                return true;
            }

            var hasLower = false;
            var hasUpper = false;
            var lower = 0UL;
            var upper = ulong.MaxValue;

            if (rowIndex > 0)
            {
                var previous = tags[rowIndex - 1];
                if (previous == null || !AssetTagsManager.TagSortOrder.TryParseOrderKey(previous.orderKey, out lower))
                    return false;
                hasLower = true;
            }

            if (rowIndex < tags.Count - 1)
            {
                var next = tags[rowIndex + 1];
                if (next == null || !AssetTagsManager.TagSortOrder.TryParseOrderKey(next.orderKey, out upper))
                    return false;
                hasUpper = true;
            }

            ulong nextKey;
            if (hasLower && hasUpper)
            {
                if (upper <= lower || upper - lower <= 1UL)
                    return false;
                nextKey = lower + (upper - lower) / 2UL;
            }
            else if (hasLower)
            {
                if (lower > ulong.MaxValue - AssetTagsManager.TagSortOrder.KeyStep)
                    return false;
                nextKey = lower + AssetTagsManager.TagSortOrder.KeyStep;
            }
            else if (hasUpper)
            {
                if (upper <= 1UL)
                    return false;
                nextKey = upper / 2UL;
            }
            else
            {
                nextKey = 0x1000UL;
            }

            row.orderKey = AssetTagsManager.TagSortOrder.FormatOrderKey(nextKey);
            row.StampOrderUpdate();
            return true;
        }

        void UpdateOrderIndicesFromPhysicalOrder()
        {
            for (var i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                if (t == null || string.IsNullOrWhiteSpace(t.tagName))
                    continue;
                t.order = i;
            }
        }

        void RedistributeAllTagOrderKeysFromPhysicalOrder()
        {
            ulong nextKey = 0x1000UL;
            for (var i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                if (t == null || string.IsNullOrWhiteSpace(t.tagName))
                    continue;
                t.order = i;
                t.orderKey = AssetTagsManager.TagSortOrder.FormatOrderKey(nextKey);
                t.StampOrderUpdate();
                nextKey += AssetTagsManager.TagSortOrder.KeyStep;
            }
        }

        void UpdateOrderMetadataForChangedRange(IReadOnlyList<TagInfo> previousOrder)
        {
            if (previousOrder == null || previousOrder.Count != tags.Count)
            {
                RedistributeAllTagOrderKeysFromPhysicalOrder();
                return;
            }

            var anyMoved = false;
            for (var i = 0; i < tags.Count; i++)
            {
                if (ReferenceEquals(previousOrder[i], tags[i]))
                    continue;
                anyMoved = true;
                break;
            }

            if (!anyMoved)
                return;

            RedistributeAllTagOrderKeysFromPhysicalOrder();
        }

        public void SetTagColor(string tag, Color color)
        {
            var row = tags.Find(x => MatchesTagName(x, tag));
            if (row != null)
            {
                row.color = color;
                row.StampTag();
            }
            else
            {
                var n = new TagInfo(tag.Trim(), color)
                {
                    tagId = AssetTagsTagId.NewTagId(),
                };
                n.StampTag();
                tags.Add(n);
                AssetTagsManager.TagSortOrder.StampNewTagAtEnd(this, n);
            }
        }

        public Color GetTagColor(string tag)
        {
            var row = tags.Find(x => MatchesTagName(x, tag));
            return row?.color ?? new Color(0.19f, 0.38f, 0.77f, 1f);
        }

        public bool TryGetTagColor(string tag, out Color color)
        {
            color = default;
            var row = tags.Find(x => MatchesTagName(x, tag));
            if (row == null)
                return false;
            color = row.color;
            return true;
        }

        public bool Contains(string tag) =>
            !string.IsNullOrWhiteSpace(tag) && tags.Exists(x => MatchesTagName(x, tag));

        public void ReplaceOrAddListEntry(TagInfo entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.tagName))
                return;
            var name = entry.tagName.Trim();
            TagInfo existing = null;
            if (AssetTagsTagId.IsWellFormed(entry.tagId))
                existing = tags.Find(x =>
                    x != null
                    && AssetTagsTagId.IsWellFormed(x.tagId)
                    && string.Equals(x.tagId, entry.tagId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                existing = tags.Find(x => MatchesTagName(x, name));
            if (existing != null)
            {
                existing.tagName = name;
                existing.color = entry.color;
                existing.tagUpdatedAt = entry.tagUpdatedAt;
                existing.tagUpdatedBy = entry.tagUpdatedBy;
                if (AssetTagsTagId.IsWellFormed(entry.tagId))
                    existing.tagId = entry.tagId.Trim();
                else if (!AssetTagsTagId.IsWellFormed(existing.tagId))
                    existing.tagId = AssetTagsTagId.NewTagId();
                if (AssetTagsManager.TagSortOrder.IncomingOrderKeyWins(existing, entry, true))
                {
                    if (!string.IsNullOrWhiteSpace(entry.orderKey))
                        existing.orderKey = entry.orderKey.Trim();
                    if (!string.IsNullOrWhiteSpace(entry.orderUpdatedAt))
                        existing.orderUpdatedAt = entry.orderUpdatedAt;
                    if (!string.IsNullOrWhiteSpace(entry.orderUpdatedBy))
                        existing.orderUpdatedBy = entry.orderUpdatedBy;
                    existing.order = entry.order;
                }
            }
            else
            {
                var copy = new TagInfo(name, entry.color)
                {
                    tagId = AssetTagsTagId.IsWellFormed(entry.tagId) ? entry.tagId.Trim() : AssetTagsTagId.NewTagId(),
                    tagUpdatedAt = entry.tagUpdatedAt,
                    tagUpdatedBy = entry.tagUpdatedBy,
                    orderKey = string.IsNullOrWhiteSpace(entry.orderKey) ? null : entry.orderKey.Trim(),
                    orderUpdatedAt = entry.orderUpdatedAt,
                    orderUpdatedBy = entry.orderUpdatedBy,
                    order = entry.order,
                };
                tags.Add(copy);
                if (!AssetTagsManager.TagSortOrder.TryParseOrderKey(copy.orderKey, out _))
                    AssetTagsManager.TagSortOrder.StampNewTagAtEnd(this, copy);
            }
        }

        bool HasTag(string tag) =>
            tags.Exists(x => MatchesTagName(x, tag));
    }
}
#endif
