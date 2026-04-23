using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public sealed class AssetTagsSetTagsPopup : PopupWindowContent
    {
        static class PopupUiLayout
        {
            public const float Width = 240f;
            public const float MaxHeight = 320f;
            public const float MinHeight = 120f;

            public const float TopPadding = 4f;
            public const float TopHorizontalPadding = 4f;
            public const float ListHorizontalPadding = 4f;
            public const float SearchToListSpacing = 2f;
            public const float BottomPadding = 2f;

            public const float RowHeight = 18f;
            public const float RowInnerPadding = 2f;

            public const float ToggleWidth = 18f;
            public const float ActionWidth = 18f;
            public const float ActionSpacing = 1f;
            public const float SearchAddSpacing = 1f;
            public const float PopupOffsetY = 2f;

            public static readonly Color IconColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            public static readonly Color IconHoverColor = Color.white;
        }

        readonly string[] targetGuids;
        readonly Dictionary<string, bool> sharedTags = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> partialTags = new Dictionary<string, bool>();

        List<string> allTags = new List<string>();
        List<string> visibleTags = new List<string>();

        ReorderableList tagList;
        Vector2 listScroll;
        string searchText = string.Empty;

        int editingTagIndex = -1;
        string editingTagName = string.Empty;
        bool focusRenameField;

        GUIStyle actionButtonStyle;
        GUIStyle addButtonStyle;

        GUIContent addIcon;
        GUIContent upIcon;
        GUIContent downIcon;
        GUIContent deleteIcon;

        public AssetTagsSetTagsPopup(string[] guids)
        {
            targetGuids = guids ?? new string[0];
        }

        public static void Show(Rect activatorRect, string[] guids)
        {
            var anchor = new Rect(
                activatorRect.xMin,
                activatorRect.yMax + PopupUiLayout.PopupOffsetY,
                Mathf.Max(1f, activatorRect.width),
                Mathf.Max(1f, activatorRect.height));
            PopupWindow.Show(anchor, new AssetTagsSetTagsPopup(guids));
        }

        public override Vector2 GetWindowSize() => new Vector2(PopupUiLayout.Width, CalculateHeight());

        public override void OnOpen()
        {
            EnsureIcons();
            Reload();
            EnsureList();
        }

        public override void OnGUI(Rect rect)
        {
            HandleRenameHotkey();
            DrawSearchBar();
            GUILayout.Space(PopupUiLayout.SearchToListSpacing);
            DrawTagList();
        }

        float CalculateHeight()
        {
            var searchHeight = EditorGUIUtility.singleLineHeight + 2f;
            var rowCount = Mathf.Max(1, GetVisibleTagCount());
            var listHeight = rowCount * PopupUiLayout.RowHeight + PopupUiLayout.BottomPadding;
            var height =
                PopupUiLayout.TopPadding +
                searchHeight +
                PopupUiLayout.SearchToListSpacing +
                listHeight;
            return Mathf.Clamp(height, PopupUiLayout.MinHeight, PopupUiLayout.MaxHeight);
        }

        int GetVisibleTagCount()
        {
            if (allTags == null || allTags.Count == 0)
                return 0;
            if (string.IsNullOrWhiteSpace(searchText))
                return allTags.Count;
            return allTags.Count(tag => MatchesSearch(tag));
        }

        void DrawSearchBar()
        {
            GUILayout.Space(PopupUiLayout.TopPadding);

            var controlHeight = EditorGUIUtility.singleLineHeight + 2f;
            var rowRect = GUILayoutUtility.GetRect(0f, controlHeight, GUILayout.ExpandWidth(true));
            rowRect.xMin += PopupUiLayout.TopHorizontalPadding;
            rowRect.xMax -= PopupUiLayout.TopHorizontalPadding;

            var addWidth = GetScrollbarWidth();
            var addRect = new Rect(rowRect.xMax - addWidth, rowRect.y, addWidth, controlHeight);
            var searchRect = new Rect(
                rowRect.x,
                rowRect.y,
                Mathf.Max(1f, rowRect.width - addWidth - PopupUiLayout.SearchAddSpacing),
                controlHeight);

            GUI.SetNextControlName("AssetTagsSetTagsPopupSearch");
            searchText = EditorGUI.TextField(searchRect, searchText, EditorStyles.toolbarSearchField);

            var previousColor = GUI.contentColor;
            GUI.contentColor = addRect.Contains(Event.current.mousePosition) ? PopupUiLayout.IconHoverColor : PopupUiLayout.IconColor;
            if (GUI.Button(addRect, AddIcon, AddButtonStyle))
                AddTagFromSearch();
            GUI.contentColor = previousColor;

            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "AssetTagsSetTagsPopupSearch")
            {
                AddTagFromSearch();
                Event.current.Use();
            }
        }

        float GetScrollbarWidth()
        {
            var style = GUI.skin?.verticalScrollbar;
            var width = style != null ? style.fixedWidth : 0f;
            if (width <= 0f)
                width = 13f;
            return Mathf.Max(10f, width);
        }

        void DrawTagList()
        {
            EnsureList();
            UpdateVisibleTags();

            GUILayout.BeginHorizontal();
            GUILayout.Space(PopupUiLayout.ListHorizontalPadding);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            tagList.DoLayoutList();
            EditorGUILayout.EndScrollView();
            GUILayout.Space(PopupUiLayout.ListHorizontalPadding);
            GUILayout.EndHorizontal();
        }

        void EnsureList()
        {
            if (tagList != null)
                return;

            tagList = new ReorderableList(visibleTags, typeof(string), true, false, false, false)
            {
                drawElementCallback = DrawListElement,
                elementHeightCallback = GetListElementHeight,
                onReorderCallbackWithDetails = OnReorder,
                headerHeight = 0f,
                footerHeight = 0f,
            };
        }

        float GetListElementHeight(int index)
        {
            if (index < 0 || index >= visibleTags.Count)
                return 0f;
            return PopupUiLayout.RowHeight;
        }

        void DrawListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= visibleTags.Count)
                return;

            var tag = visibleTags[index];
            var globalIndex = allTags.IndexOf(tag);
            DrawTagRow(rect, globalIndex, tag);
        }

        void DrawTagRow(Rect rect, int globalIndex, string tag)
        {
            AssetTagsTagStyle.GetTagDimensions(tag, out _, out var chipHeight);

            var controlHeight = Mathf.Min(EditorGUIUtility.singleLineHeight, rect.height - PopupUiLayout.RowInnerPadding);
            var controlY = rect.y + Mathf.Max(0f, (rect.height - controlHeight) * 0.5f);

            var actionX = rect.xMax - PopupUiLayout.ActionWidth;
            var deleteRect = new Rect(actionX, controlY, PopupUiLayout.ActionWidth, controlHeight);
            actionX -= PopupUiLayout.ActionWidth + PopupUiLayout.ActionSpacing;
            var downRect = new Rect(actionX, controlY, PopupUiLayout.ActionWidth, controlHeight);
            actionX -= PopupUiLayout.ActionWidth + PopupUiLayout.ActionSpacing;
            var upRect = new Rect(actionX, controlY, PopupUiLayout.ActionWidth, controlHeight);
            actionX -= PopupUiLayout.ActionWidth + PopupUiLayout.ActionSpacing;
            var colorRect = new Rect(actionX, controlY, PopupUiLayout.ActionWidth, controlHeight);

            var toggleRect = new Rect(rect.x, controlY, PopupUiLayout.ToggleWidth, controlHeight);
            var labelRect = new Rect(
                toggleRect.xMax + 2f,
                rect.y,
                Mathf.Max(70f, colorRect.xMin - toggleRect.xMax - 4f),
                rect.height);

            DrawToggle(tag, toggleRect);
            DrawTagChipOrEditor(tag, globalIndex, labelRect, chipHeight);
            DrawColorField(tag, colorRect);
            DrawMoveButtons(tag, upRect, downRect);
            DrawDeleteButton(tag, deleteRect);
        }

        void DrawTagChipOrEditor(string tag, int globalIndex, Rect rect, float chipHeight)
        {
            if (editingTagIndex == globalIndex)
            {
                var controlName = $"AssetTagsSetTagsPopupRename_{globalIndex}";
                GUI.SetNextControlName(controlName);
                var next = EditorGUI.TextField(rect, editingTagName);
                if (next != editingTagName)
                    editingTagName = next;

                if (focusRenameField && Event.current.type == EventType.Repaint)
                {
                    EditorGUI.FocusTextInControl(controlName);
                    focusRenameField = false;
                }

                if (Event.current.type == EventType.KeyDown)
                {
                    if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                    {
                        CommitRename(globalIndex);
                        Event.current.Use();
                    }
                    else if (Event.current.keyCode == KeyCode.Escape)
                    {
                        CancelRename();
                        Event.current.Use();
                    }
                }

                if (Event.current.type == EventType.MouseDown && !rect.Contains(Event.current.mousePosition))
                    CommitRename(globalIndex);

                return;
            }

            var color = AssetTagsManager.Instance.GetTagColor(tag);
            AssetTagsTagStyle.GetTagDimensions(tag, out var tagWidth, out _);
            var chipRect = new Rect(
                rect.x,
                rect.y + Mathf.Max(0f, (rect.height - chipHeight) * 0.5f),
                Mathf.Min(tagWidth, rect.width),
                chipHeight);
            AssetTagsTagStyle.DrawTintedTag(chipRect, tag, color);

            if (Event.current.type == EventType.MouseDown
                && Event.current.clickCount == 2
                && chipRect.Contains(Event.current.mousePosition))
            {
                BeginRename(globalIndex);
                Event.current.Use();
            }
        }

        void DrawToggle(string tag, Rect rect)
        {
            var isShared = sharedTags.ContainsKey(tag);
            var isPartial = partialTags.ContainsKey(tag);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = isPartial;
            var next = EditorGUI.Toggle(rect, isShared);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck())
                return;

            for (var i = 0; i < targetGuids.Length; i++)
            {
                if (next)
                    AssetTagsManager.Instance.AddTag(targetGuids[i], tag);
                else
                    AssetTagsManager.Instance.RemoveTag(targetGuids[i], tag);
            }

            ReloadAndRepaint();
        }

        void DrawColorField(string tag, Rect rect)
        {
            var current = AssetTagsManager.Instance.GetTagColor(tag);
            var next = EditorGUI.ColorField(rect, GUIContent.none, current, false, false, false);
            if (next == current)
                return;

            AssetTagsManager.Instance.SetTagColor(tag, next);
            EditorApplication.RepaintProjectWindow();
        }

        void DrawMoveButtons(string tag, Rect upRect, Rect downRect)
        {
            if (GUI.Button(upRect, UpIcon, ActionButtonStyle))
            {
                AssetTagsManager.Instance.MoveTagUp(tag);
                ReloadAndRepaint();
            }

            if (GUI.Button(downRect, DownIcon, ActionButtonStyle))
            {
                AssetTagsManager.Instance.MoveTagDown(tag);
                ReloadAndRepaint();
            }
        }

        void DrawDeleteButton(string tag, Rect rect)
        {
            if (!GUI.Button(rect, DeleteIcon, ActionButtonStyle))
                return;

            var usageCount = CountAssetsUsingTag(tag);
            if (usageCount > 0)
            {
                var isConfirmed = EditorUtility.DisplayDialog(
                    "Delete Tag",
                    $"The tag \"{tag}\" is currently used by {usageCount} asset(s).\n\nDo you want to delete this tag from all assets?",
                    "Yes",
                    "Cancel");

                if (!isConfirmed)
                    return;
            }

            AssetTagsManager.Instance.DeleteTag(tag);
            ReloadAndRepaint();
        }

        static int CountAssetsUsingTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return 0;

            var count = 0;
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                    continue;

                if (AssetTagsManager.Instance.GetTags(guid).Contains(tag))
                    count++;
            }

            return count;
        }

        void AddTagFromSearch()
        {
            var tag = string.IsNullOrWhiteSpace(searchText) ? string.Empty : searchText.Trim();
            if (string.IsNullOrEmpty(tag))
                return;

            for (var i = 0; i < targetGuids.Length; i++)
                AssetTagsManager.Instance.AddTag(targetGuids[i], tag);

            searchText = string.Empty;
            ReloadAndRepaint();
        }

        bool MatchesSearch(string tag)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;
            return tag.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void HandleRenameHotkey()
        {
            if (Event.current.type != EventType.KeyDown || Event.current.keyCode != KeyCode.F2)
                return;
            if (tagList == null || tagList.index < 0 || tagList.index >= visibleTags.Count)
                return;

            var selectedTag = visibleTags[tagList.index];
            BeginRename(allTags.IndexOf(selectedTag));
            Event.current.Use();
        }

        void BeginRename(int globalIndex)
        {
            if (globalIndex < 0 || globalIndex >= allTags.Count)
                return;

            editingTagIndex = globalIndex;
            editingTagName = allTags[globalIndex];
            focusRenameField = true;
        }

        void CancelRename()
        {
            editingTagIndex = -1;
            editingTagName = string.Empty;
            focusRenameField = false;
        }

        void CommitRename(int globalIndex)
        {
            if (globalIndex < 0 || globalIndex >= allTags.Count)
            {
                CancelRename();
                return;
            }

            var oldName = allTags[globalIndex];
            var newName = string.IsNullOrWhiteSpace(editingTagName) ? string.Empty : editingTagName.Trim();
            if (!string.IsNullOrEmpty(newName) && newName != oldName)
            {
                AssetTagsManager.Instance.RenameTag(oldName, newName);
                ReloadAndRepaint();
            }

            CancelRename();
        }

        void OnReorder(ReorderableList list, int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex || oldIndex < 0 || oldIndex >= visibleTags.Count)
                return;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                var moved = visibleTags[newIndex];
                MoveTagToIndex(moved, newIndex);
            }
            else
            {
                ReorderWithinSearchResult(oldIndex, newIndex);
            }

            ReloadAndRepaint();
        }

        void ReorderWithinSearchResult(int oldIndex, int newIndex)
        {
            var changeStart = Mathf.Min(oldIndex, newIndex);
            if (changeStart < 0 || changeStart >= visibleTags.Count)
                return;

            var before = new List<string>(visibleTags);
            var moved = before[newIndex];
            before.RemoveAt(newIndex);
            before.Insert(oldIndex, moved);

            var segmentBefore = before.Skip(changeStart).ToList();
            var segmentAfter = visibleTags.Skip(changeStart).ToList();
            if (segmentBefore.Count == 0 || segmentAfter.Count == 0)
                return;

            var startTag = segmentBefore[0];
            var endTag = segmentBefore[segmentBefore.Count - 1];
            var startIndex = allTags.IndexOf(startTag);
            var endIndex = allTags.IndexOf(endTag);
            if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
                return;

            var changed = new HashSet<string>(segmentBefore);
            var untouched = new List<string>();
            for (var i = startIndex; i <= endIndex; i++)
            {
                var tag = allTags[i];
                if (!changed.Contains(tag))
                    untouched.Add(tag);
            }

            var targetOrder = new List<string>(allTags);
            var writeIndex = startIndex;
            for (var i = 0; i < segmentAfter.Count; i++)
                targetOrder[writeIndex++] = segmentAfter[i];
            for (var i = 0; i < untouched.Count; i++)
                targetOrder[writeIndex++] = untouched[i];

            ApplyGlobalOrder(targetOrder);
        }

        void ApplyGlobalOrder(List<string> targetOrder)
        {
            for (var index = 0; index < targetOrder.Count; index++)
                MoveTagToIndex(targetOrder[index], index);
        }

        void MoveTagToIndex(string tag, int targetIndex)
        {
            var order = AssetTagsManager.Instance.GetAllAvailableTags();
            var currentIndex = order.IndexOf(tag);
            if (currentIndex < 0)
                return;

            while (currentIndex > targetIndex)
            {
                AssetTagsManager.Instance.MoveTagUp(tag);
                currentIndex--;
            }

            while (currentIndex < targetIndex)
            {
                AssetTagsManager.Instance.MoveTagDown(tag);
                currentIndex++;
            }
        }

        void ReloadAndRepaint()
        {
            Reload();
            EditorApplication.RepaintProjectWindow();
            editorWindow?.Repaint();
        }

        void Reload()
        {
            allTags = AssetTagsManager.Instance.GetAllAvailableTags();
            RefreshSelectionState();
            UpdateVisibleTags();

            if (tagList != null)
                tagList.list = visibleTags;
        }

        void UpdateVisibleTags()
        {
            visibleTags = string.IsNullOrWhiteSpace(searchText)
                ? new List<string>(allTags)
                : allTags.Where(MatchesSearch).ToList();

            if (tagList != null)
                tagList.list = visibleTags;
        }

        void RefreshSelectionState()
        {
            sharedTags.Clear();
            partialTags.Clear();
            if (targetGuids.Length == 0)
                return;

            var first = AssetTagsManager.Instance.GetTags(targetGuids[0]);
            for (var i = 0; i < first.Count; i++)
                sharedTags[first[i]] = true;

            for (var i = 1; i < targetGuids.Length; i++)
            {
                var tags = AssetTagsManager.Instance.GetTags(targetGuids[i]);
                var remove = new List<string>();

                foreach (var tag in sharedTags.Keys)
                {
                    if (tags.Contains(tag))
                        continue;
                    remove.Add(tag);
                    partialTags[tag] = true;
                }

                for (var r = 0; r < remove.Count; r++)
                    sharedTags.Remove(remove[r]);

                for (var t = 0; t < tags.Count; t++)
                {
                    var tag = tags[t];
                    if (!sharedTags.ContainsKey(tag) && !partialTags.ContainsKey(tag))
                        partialTags[tag] = true;
                }
            }
        }

        void EnsureIcons()
        {
            addIcon = LoadIconOrFallback(new[] { "Toolbar Plus" }, "+", "Add tag");
            upIcon = LoadIconOrFallback(new[] { "HoverBar_Up" }, "\u25b2", "Move tag up");
            downIcon = LoadIconOrFallback(new[] { "HoverBar_Down" }, "\u25bc", "Move tag down");
            deleteIcon = LoadIconOrFallback(new[] { "CrossIcon" }, "X", "Delete tag");
        }

        static GUIContent LoadIconOrFallback(string[] keys, string fallback, string tooltip)
        {
            for (var i = 0; i < keys.Length; i++)
            {
                var content = EditorGUIUtility.IconContent(keys[i], tooltip);
                if (content != null && content.image != null)
                    return content;
            }

            return new GUIContent(fallback, tooltip);
        }

        GUIStyle ActionButtonStyle
        {
            get
            {
                if (actionButtonStyle != null)
                    return actionButtonStyle;

                actionButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 8,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };
                return actionButtonStyle;
            }
        }

        GUIStyle AddButtonStyle
        {
            get
            {
                if (addButtonStyle != null)
                    return addButtonStyle;

                addButtonStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    imagePosition = ImagePosition.ImageOnly,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };
                addButtonStyle.normal.background = null;
                addButtonStyle.hover.background = null;
                addButtonStyle.active.background = null;
                addButtonStyle.focused.background = null;
                return addButtonStyle;
            }
        }

        GUIContent AddIcon => addIcon ?? (addIcon = new GUIContent("+", "Add tag"));
        GUIContent UpIcon => upIcon ?? (upIcon = new GUIContent("\u25b2", "Move tag up"));
        GUIContent DownIcon => downIcon ?? (downIcon = new GUIContent("\u25bc", "Move tag down"));
        GUIContent DeleteIcon => deleteIcon ?? (deleteIcon = new GUIContent("X", "Delete tag"));
    }
}
