#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public sealed class AssetTagsList
    {
        static readonly StringComparer TagComparer = StringComparer.OrdinalIgnoreCase;

        [Serializable]
        public sealed class TagInfo
        {
            public string tagName;
            public Color color;
            public string lastModifiedAtUtc;
            public string lastModifiedBy;

            public TagInfo(string name, Color col)
            {
                tagName = name;
                color = col;
            }

            internal void StampModification()
            {
                lastModifiedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                lastModifiedBy = AssetTagsJsonRepository.ResolveLastModifiedBy();
            }
        }

        public List<TagInfo> tags = new List<TagInfo>();

        public List<string> GetAvailableTags()
        {
            var result = new List<string>(tags.Count);
            foreach (var entry in tags)
                result.Add(entry.tagName);
            return result;
        }

        public void AddTag(string tag)
        {
            if (!HasTag(tag))
            {
                var row = new TagInfo(tag.Trim(), AssetTagsColorUtilities.GenerateTagColor());
                row.StampModification();
                tags.Add(row);
            }
        }

        public void RemoveTag(string tag) =>
            tags.RemoveAll(x => TagComparer.Equals(x.tagName, tag));

        public void RenameTag(string oldTag, string newTag)
        {
            if (TagComparer.Equals(oldTag, newTag) || HasTag(newTag))
                return;
            var row = tags.Find(x => TagComparer.Equals(x.tagName, oldTag));
            if (row != null)
            {
                row.tagName = newTag.Trim();
                row.StampModification();
            }
        }

        public void MoveTagUp(string tag)
        {
            var index = tags.FindIndex(x => TagComparer.Equals(x.tagName, tag));
            if (index <= 0)
                return;
            (tags[index], tags[index - 1]) = (tags[index - 1], tags[index]);
        }

        public void MoveTagDown(string tag)
        {
            var index = tags.FindIndex(x => TagComparer.Equals(x.tagName, tag));
            if (index < 0 || index >= tags.Count - 1)
                return;
            (tags[index], tags[index + 1]) = (tags[index + 1], tags[index]);
        }

        public void SetTagColor(string tag, Color color)
        {
            var row = tags.Find(x => TagComparer.Equals(x.tagName, tag));
            if (row != null)
            {
                row.color = color;
                row.StampModification();
            }
            else
            {
                var n = new TagInfo(tag.Trim(), color);
                n.StampModification();
                tags.Add(n);
            }
        }

        public Color GetTagColor(string tag)
        {
            var row = tags.Find(x => TagComparer.Equals(x.tagName, tag));
            return row?.color ?? new Color(0.19f, 0.38f, 0.77f, 1f);
        }

        public void ReplaceOrAddListEntry(TagInfo entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.tagName))
                return;
            var name = entry.tagName.Trim();
            var existing = tags.Find(x => TagComparer.Equals(x.tagName, name));
            if (existing != null)
            {
                existing.tagName = name;
                existing.color = entry.color;
                existing.lastModifiedAtUtc = entry.lastModifiedAtUtc;
                existing.lastModifiedBy = entry.lastModifiedBy;
            }
            else
            {
                var copy = new TagInfo(name, entry.color)
                {
                    lastModifiedAtUtc = entry.lastModifiedAtUtc,
                    lastModifiedBy = entry.lastModifiedBy,
                };
                tags.Add(copy);
            }
        }

        bool HasTag(string tag) =>
            tags.Exists(x => TagComparer.Equals(x.tagName, tag));
    }
}
#endif
