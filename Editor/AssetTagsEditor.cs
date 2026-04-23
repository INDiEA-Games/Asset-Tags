using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    [InitializeOnLoad]
    public class AssetTagsEditor
    {
        static class ProjectRowUi
        {
            public const float AddButtonSize = 16f;
            public const float TagSpacing = 4f;
            public const float IconToNameSpacing = 6f;
            public const float NameToMaskSpacing = 4f;
            public const float MinTagVisibleWidth = 8f;
            public static readonly Color AddButtonNormalContentColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            public static readonly Color AddButtonHoverContentColor = Color.white;
        }

        static readonly List<string> orderedTags = new List<string>();
        static readonly Dictionary<string, Color> tagColorCache = new Dictionary<string, Color>();

        static GUIStyle nameMeasureStyle;
        static GUIStyle addButtonStyle;
        static GUIContent addButtonContent;

        static AssetTagsEditor()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGui;
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssetTagsManager.OnTagsChanged += OnTagsChanged;
        }

        public static void InvalidateTagCache() => RefreshTagOrder();

        public static Color GetTagColor(string tag)
        {
            if (tagColorCache.TryGetValue(tag, out var color))
                return color;

            color = AssetTagsManager.Instance.GetTagColor(tag);
            tagColorCache[tag] = color;
            return color;
        }

        static void OnProjectChanged() => InvalidateTagCache();

        static void OnUndoRedo() => InvalidateTagCache();

        static void OnTagsChanged()
        {
            InvalidateTagCache();
            tagColorCache.Clear();
        }

        static void RefreshTagOrder()
        {
            var latest = AssetTagsManager.Instance.GetAllAvailableTags();
            if (orderedTags.SequenceEqual(latest))
                return;

            orderedTags.Clear();
            orderedTags.AddRange(latest);
            EditorApplication.RepaintProjectWindow();
        }

        static void OnProjectWindowItemGui(string guid, Rect rowRect)
        {
            if (rowRect.height > 20f)
                return;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return;

            var addRect = CreateAddButtonRect(rowRect);
            var tags = GetOrderedTags(guid);
            if (tags.Count > 0 && Event.current.type == EventType.Repaint)
                DrawTags(rowRect, addRect, path, tags);

            var isHover = addRect.Contains(Event.current.mousePosition);
            var previousContentColor = GUI.contentColor;
            GUI.contentColor = isHover
                ? ProjectRowUi.AddButtonHoverContentColor
                : ProjectRowUi.AddButtonNormalContentColor;

            var pressed = GUI.Button(addRect, AddButtonContent, AddButtonStyle);
            GUI.contentColor = previousContentColor;

            if (pressed)
                AssetTagsSetTagsPopup.Show(addRect, GetPopupTargetGuids(guid));
        }

        static Rect CreateAddButtonRect(Rect rowRect)
        {
            var size = Mathf.Clamp(
                Mathf.Min(ProjectRowUi.AddButtonSize, rowRect.height - 2f),
                12f,
                ProjectRowUi.AddButtonSize);

            return new Rect(
                rowRect.xMax - size,
                rowRect.y + (rowRect.height - size) * 0.5f,
                size,
                size);
        }

        static List<string> GetOrderedTags(string guid)
        {
            var tags = AssetTagsManager.Instance.GetTags(guid);
            var order = AssetTagsManager.Instance.GetAllAvailableTags();
            return tags.OrderBy(tag => order.IndexOf(tag)).ToList();
        }

        static void DrawTags(Rect rowRect, Rect addRect, string path, List<string> tags)
        {
            var labelStartX = GetLabelStartX(rowRect);
            var displayName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(displayName))
                displayName = path;

            var maxNameWidth = Mathf.Max(
                8f,
                rowRect.xMax - labelStartX - addRect.width - ProjectRowUi.NameToMaskSpacing);
            var nameWidth = MeasureNameWidth(displayName, maxNameWidth);

            var maskLeft = Mathf.Round(labelStartX + nameWidth + ProjectRowUi.NameToMaskSpacing);
            var maskRight = Mathf.Round(addRect.x - ProjectRowUi.TagSpacing);
            var maskWidth = maskRight - maskLeft;
            if (maskWidth <= 0.5f)
                return;

            var totalTagWidth = GetTotalTagWidth(tags);
            GUI.BeginGroup(new Rect(maskLeft, rowRect.y, maskWidth, rowRect.height));
            DrawTagsInMask(rowRect.height, tags, maskWidth, totalTagWidth);
            GUI.EndGroup();
        }

        static void DrawTagsInMask(float rowHeight, List<string> tags, float maskWidth, float totalTagWidth)
        {
            var x = maskWidth - totalTagWidth;
            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                AssetTagsTagStyle.GetTagDimensions(tag, out var tagWidth, out var tagHeight);
                var tagRect = new Rect(
                    x,
                    Mathf.Max(0f, (rowHeight - tagHeight) * 0.5f),
                    tagWidth,
                    tagHeight);

                if (tagRect.x >= maskWidth)
                    break;

                if (IsTagVisible(tagRect, maskWidth))
                    AssetTagsTagStyle.DrawTintedTagForProjectRow(tagRect, tag, GetTagColor(tag));

                x += tagWidth + ProjectRowUi.TagSpacing;
            }
        }

        static bool IsTagVisible(Rect tagRect, float maskWidth)
        {
            var visibleLeft = Mathf.Max(0f, tagRect.x);
            var visibleRight = Mathf.Min(maskWidth, tagRect.xMax);
            var visibleWidth = visibleRight - visibleLeft;
            return visibleWidth >= ProjectRowUi.MinTagVisibleWidth;
        }

        static float GetTotalTagWidth(List<string> tags)
        {
            var width = 0f;
            for (var i = 0; i < tags.Count; i++)
            {
                AssetTagsTagStyle.GetTagDimensions(tags[i], out var tagWidth, out _);
                width += tagWidth;
                if (i > 0)
                    width += ProjectRowUi.TagSpacing;
            }

            return width;
        }

        static float GetLabelStartX(Rect rowRect)
        {
            var iconWidth = Mathf.Clamp(EditorGUIUtility.singleLineHeight - 2f, 14f, 24f);
            return rowRect.x + iconWidth + ProjectRowUi.IconToNameSpacing;
        }

        static float MeasureNameWidth(string name, float maxWidth)
        {
            if (string.IsNullOrEmpty(name) || maxWidth < 1f)
                return 0f;

            var fullWidth = NameMeasureStyle.CalcSize(new GUIContent(name)).x;
            if (fullWidth <= maxWidth)
                return fullWidth;

            const string ellipsis = "\u2026";
            for (var length = name.Length; length >= 1; length--)
            {
                var clipped = name.Substring(0, length) + ellipsis;
                var clippedWidth = NameMeasureStyle.CalcSize(new GUIContent(clipped)).x;
                if (clippedWidth <= maxWidth)
                    return clippedWidth;
            }

            return maxWidth;
        }

        static GUIStyle NameMeasureStyle =>
            nameMeasureStyle ?? (nameMeasureStyle = new GUIStyle(EditorStyles.label) { wordWrap = false });

        static GUIContent AddButtonContent
        {
            get
            {
                if (addButtonContent != null)
                    return addButtonContent;

                var icon = EditorGUIUtility.IconContent("Toolbar Plus", "Add or edit tags");
                addButtonContent = icon != null && icon.image != null
                    ? icon
                    : new GUIContent("+", "Add or edit tags");
                return addButtonContent;
            }
        }

        static GUIStyle AddButtonStyle
        {
            get
            {
                if (addButtonStyle != null)
                    return addButtonStyle;

                addButtonStyle = new GUIStyle(EditorStyles.label)
                {
                    padding = new RectOffset(0, 0, 0, 0),
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter,
                    fixedHeight = 0f,
                    fixedWidth = 0f,
                };
                return addButtonStyle;
            }
        }

        static string[] GetPopupTargetGuids(string rowGuid)
        {
            var selected = Selection.assetGUIDs;
            if (selected == null || selected.Length == 0)
                return new[] { rowGuid };

            for (var i = 0; i < selected.Length; i++)
            {
                if (selected[i] == rowGuid)
                    return selected;
            }

            return new[] { rowGuid };
        }
    }
}
