using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    internal static class AssetTagsTagStyle
    {
        const string TagTextureAssetPath = "Assets/INDiEA/Asset Tags/Resources/Texture_AssetTags.png";
        public static Vector2 textOffset = new Vector2(3f, 0f);
        static GUIStyle tagStyle;
        static GUIStyle projectRowTagDrawStyle;
        static Texture2D tagTexture;
        static bool tagTextureDirty = true;
        static RectOffset tagBorder;
        static Hash128 tagTextureHash;

        public static GUIStyle GetStyle()
        {
            var texture = LoadTagTexture();
            if (tagStyle == null || tagStyle.normal.background != texture)
            {
                tagStyle = new GUIStyle(GUIStyle.none)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    fontStyle = FontStyle.Normal,
                    fontSize = 10,
                    border = tagBorder ?? new RectOffset(0, 0, 0, 0),
                };
                tagStyle.normal.textColor = Color.white;
                tagStyle.hover.textColor = Color.white;
                tagStyle.active.textColor = Color.white;
                tagStyle.normal.background = texture;
                tagStyle.hover.background = texture;
                tagStyle.active.background = texture;
                tagStyle.focused.background = texture;
            }

            tagStyle.contentOffset = textOffset;
            return tagStyle;
        }

        public static void GetTagDimensions(string tag, out float tagWidth, out float tagHeight)
        {
            var style = GetStyle();
            var size = style.CalcSize(new GUIContent(tag));
            var border = style.border ?? new RectOffset(0, 0, 0, 0);
            var centerWidth = Mathf.Max(1f, size.x + 2f);
            tagWidth = Mathf.Max(1f, centerWidth + border.left + border.right);
            tagWidth += Mathf.Abs(textOffset.x);
            tagHeight = Mathf.Max(1f, size.y + 2f);
        }

        public static void DrawTintedTag(Rect tagRect, string tag, Color tint)
        {
            DrawTintedTagImpl(tagRect, tag, tint, TextAnchor.MiddleCenter);
        }

        public static void DrawTintedTagForProjectRow(Rect tagRect, string tag, Color tint)
        {
            DrawTintedTagImpl(tagRect, tag, tint, TextAnchor.MiddleRight);
        }

        static void DrawTintedTagImpl(Rect tagRect, string tag, Color tint, TextAnchor alignment)
        {
            tint.a = 1f;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            var baseStyle = GetStyle();
            GUIStyle drawStyle;
            if (alignment == TextAnchor.MiddleCenter)
                drawStyle = baseStyle;
            else
            {
                if (projectRowTagDrawStyle == null || projectRowTagDrawStyle.normal.background != baseStyle.normal.background)
                {
                    projectRowTagDrawStyle = new GUIStyle(baseStyle)
                    {
                        alignment = TextAnchor.MiddleRight,
                        clipping = TextClipping.Clip,
                    };
                    var pad = projectRowTagDrawStyle.padding;
                    projectRowTagDrawStyle.padding = new RectOffset(4, 6, pad.top, pad.bottom);
                }

                drawStyle = projectRowTagDrawStyle;
            }

            if (Event.current.type == EventType.Repaint)
                drawStyle.Draw(tagRect, new GUIContent(tag), false, false, false, false);
            GUI.backgroundColor = prevBg;
        }

        static Texture2D LoadTagTexture()
        {
            var hash = AssetDatabase.GetAssetDependencyHash(TagTextureAssetPath);
            var reload = tagTextureDirty || tagTexture == null || hash != tagTextureHash;
            if (!reload)
                return tagTexture;

            tagTextureDirty = false;
            tagTextureHash = hash;
            tagTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TagTextureAssetPath);
            if (tagTexture != null)
            {
                tagBorder = BorderFromSprite(TagTextureAssetPath);
                tagStyle = null;
                return tagTexture;
            }

            tagTexture = Resources.Load<Texture2D>("Texture_AssetTags");
            tagBorder = new RectOffset(0, 0, 0, 0);
            tagStyle = null;
            return tagTexture;
        }

        static RectOffset BorderFromSprite(string assetPath)
        {
            var assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            if (assets == null)
                return new RectOffset(0, 0, 0, 0);

            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not Sprite sprite)
                    continue;
                var border = sprite.border;
                return new RectOffset(
                    Mathf.RoundToInt(border.x),
                    Mathf.RoundToInt(border.z),
                    Mathf.RoundToInt(border.w),
                    Mathf.RoundToInt(border.y));
            }

            return new RectOffset(0, 0, 0, 0);
        }
    }
}
