#if UNITY_2021_2_OR_NEWER
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public sealed class AssetTagsProjectBrowserSearchFilterPopup : PopupWindowContent
    {
        const float PopupWidth = 150f;
        const float ListHeight = 245f;
        const float HorizontalPadding = 6f;
        static readonly Color HoverRow = new Color(0.24f, 0.49f, 0.90f, 1f);
        const string SearchControlName = "INDiEA.AssetTagsProjectBrowserSearchFilterPopup.Search";

        string searchText = string.Empty;
        string hoverText = string.Empty;
        string selectedTag = string.Empty;
        bool waitNextHover;
        Vector2 scrollPosition;
        bool focusSearchNextRepaint;
        public override Vector2 GetWindowSize()
        {
            var height = 4f + EditorGUIUtility.singleLineHeight + 4f + ListHeight + 4f;
            return new Vector2(PopupWidth, height);
        }

        public override void OnOpen()
        {
            searchText = string.Empty;
            selectedTag = AssetTagsProjectBrowserSearch.GetAppliedTagNeedle() ?? string.Empty;
            hoverText = string.Empty;
            waitNextHover = false;
            scrollPosition = Vector2.zero;
            focusSearchNextRepaint = true;
        }

        public override void OnGUI(Rect rect)
        {
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;

            var previousHover = hoverText;
            var contentRect = new Rect(
                rect.x + HorizontalPadding,
                rect.y,
                Mathf.Max(1f, rect.width - HorizontalPadding * 2f),
                rect.height);
            GUILayout.BeginArea(contentRect);
            GUILayout.Space(4f);
            GUI.SetNextControlName(SearchControlName);
            var hasHover = !string.IsNullOrEmpty(hoverText);
            var searchFocused = GUI.GetNameOfFocusedControl() == SearchControlName;
            var showHoverInField = hasHover && string.IsNullOrEmpty(searchText);
            var displayed = showHoverInField ? hoverText : searchText;

            var searchRect = GUILayoutUtility.GetRect(
                0f,
                EditorGUIUtility.singleLineHeight + 2f,
                EditorStyles.toolbarSearchField,
                GUILayout.ExpandWidth(true));

            var cancelRect = new Rect(searchRect.xMax - 18f, searchRect.y, 18f, searchRect.height);
            var cancel = GUI.skin.FindStyle("ToolbarSeachCancelButton");
            var cancelEmpty = GUI.skin.FindStyle("ToolbarSeachCancelButtonEmpty");
            var cancelStyle = string.IsNullOrEmpty(displayed)
                ? (cancelEmpty ?? GUIStyle.none)
                : (cancel ?? GUIStyle.none);

            if (IsCancelClick(cancelRect))
            {
                searchText = string.Empty;
                hoverText = string.Empty;
                waitNextHover = true;
                GUI.FocusControl(SearchControlName);
                editorWindow?.Repaint();
            }

            var edited = GUI.TextField(searchRect, displayed ?? string.Empty, EditorStyles.toolbarSearchField);
            GUI.Box(cancelRect, GUIContent.none, cancelStyle);

            if (focusSearchNextRepaint && Event.current.type == EventType.Repaint)
            {
                EditorGUI.FocusTextInControl(SearchControlName);
                focusSearchNextRepaint = false;
            }

            if (showHoverInField && Event.current.type == EventType.Repaint)
            {
                EditorGUI.FocusTextInControl(SearchControlName);
                SelectAllInFocusedTextField();
            }

            if (edited != displayed && (!hasHover || searchFocused))
            {
                searchText = edited ?? string.Empty;
                if (!string.IsNullOrEmpty(searchText))
                    hoverText = string.Empty;
                editorWindow?.Repaint();
            }

            GUILayout.Space(4f);

            var allTags = AssetTagsManager.Instance.GetAllAvailableTags();
            var filtered = FilterTags(allTags, searchText);
            string rowHover = null;

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(ListHeight));
            foreach (var tag in filtered)
            {
                var clicked = DrawTagRow(tag, out var hovered);
                if (hovered)
                    rowHover = tag;

                if (clicked)
                {
                    GUILayout.EndScrollView();
                    GUILayout.EndArea();
                    ApplyAndClose(tag);
                    return;
                }
            }

            EditorGUILayout.EndScrollView();

            if (Event.current.type == EventType.MouseMove)
            {
                if (waitNextHover)
                {
                    if (!string.IsNullOrEmpty(rowHover))
                    {
                        hoverText = rowHover;
                        waitNextHover = false;
                    }
                }
                else if (!string.IsNullOrEmpty(rowHover))
                {
                    hoverText = rowHover;
                }
            }

            if (!string.Equals(previousHover, hoverText, System.StringComparison.Ordinal))
                editorWindow?.Repaint();

            if (Event.current.type == EventType.MouseMove)
                editorWindow?.Repaint();

            GUILayout.EndArea();
        }

        static bool IsCancelClick(Rect cancelRect)
        {
            var e = Event.current;
            if (e.button != 0 || !cancelRect.Contains(e.mousePosition))
                return false;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseUp)
                return false;
            e.Use();
            return true;
        }

        bool DrawTagRow(string tag, out bool hovered)
        {
            var rowHeight = EditorGUIUtility.singleLineHeight + 2f;
            var rowRect = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));
            var current = Event.current.type;
            hovered = current == EventType.MouseMove && rowRect.Contains(Event.current.mousePosition);

            if (string.Equals(tag, hoverText, System.StringComparison.OrdinalIgnoreCase) && current == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, HoverRow);

            if (string.Equals(tag, selectedTag, System.StringComparison.OrdinalIgnoreCase))
            {
                var check = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold,
                };
                check.normal.textColor = Color.white;
                var checkRect = new Rect(rowRect.x + 3f, rowRect.y, 14f, rowRect.height);
                EditorGUI.LabelField(checkRect, "\u2713", check);
            }

            AssetTagsTagStyle.GetTagDimensions(tag, out var tagWidth, out var tagHeight);
            var tagRect = new Rect(
                rowRect.xMax - tagWidth - 4f,
                rowRect.y + Mathf.Max(0f, (rowRect.height - tagHeight) * 0.5f),
                tagWidth,
                tagHeight);

            var tint = AssetTagsManager.Instance.GetTagColor(tag);
            tint.a = 1f;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            var clicked = GUI.Button(tagRect, tag, AssetTagsTagStyle.GetStyle());
            GUI.backgroundColor = prevBg;

            if (clicked)
                return true;

            if (current == EventType.MouseDown
                && Event.current.button == 0
                && rowRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        static List<string> FilterTags(List<string> tags, string needle)
        {
            var result = new List<string>();
            if (tags == null || tags.Count == 0)
                return result;

            if (string.IsNullOrWhiteSpace(needle))
            {
                result.AddRange(tags);
                return result;
            }

            needle = needle.Trim();
            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (string.IsNullOrEmpty(tag))
                    continue;
                if (tag.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(tag);
            }

            return result;
        }

        static void SelectAllInFocusedTextField()
        {
            var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            editor?.SelectAll();
        }

        void ApplyAndClose(string tag)
        {
            AssetTagsProjectBrowserSearch.ApplyTagNeedle(tag);
            selectedTag = tag ?? string.Empty;
            editorWindow?.Close();
        }
    }

    public static class AssetTagsProjectBrowserFilter
    {
        public static void Show(Rect activatorRect) =>
            PopupWindow.Show(activatorRect, new AssetTagsProjectBrowserSearchFilterPopup());
    }
}
#endif
