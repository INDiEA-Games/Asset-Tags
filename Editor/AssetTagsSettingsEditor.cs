using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    [CustomEditor(typeof(AssetTagsSettings))]
    sealed class AssetTagsSettingsEditor : Editor
    {
        const string ConversionMenuRoot = "Window/INDiEA/Asset Tags/";

        [MenuItem(ConversionMenuRoot + "Convert All Asset Tags To Asset Labels", false, 10)]
        static void MenuConvertAllAssetTagsToAssetLabels() =>
            RunConvertTagsToAssetLabelsIfConfirmed();

        [MenuItem(ConversionMenuRoot + "Convert All Asset Labels To Asset Tags", false, 11)]
        static void MenuConvertAllAssetLabelsToAssetTags() =>
            RunConvertAssetLabelsToTagsIfConfirmed();

        static void RunConvertTagsToAssetLabelsIfConfirmed()
        {
            if (!EditorUtility.DisplayDialog(
                    "Convert All Asset Tags To Asset Labels",
                    "Merge every Asset Tag into each asset's Unity labels. This cannot be undone automatically. Continue?",
                    "Yes",
                    "Cancel"))
                return;
            AssetTagsManager.Instance.ConvertTagsToAssetLabels();
        }

        static void RunConvertAssetLabelsToTagsIfConfirmed()
        {
            if (!EditorUtility.DisplayDialog(
                    "Convert All Asset Labels To Asset Tags",
                    "Copy every Unity label on project assets into Asset Tags data and the global tag list. Continue?",
                    "Yes",
                    "Cancel"))
                return;
            AssetTagsManager.Instance.ConvertAssetLabelsToTags();
        }

        void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;

        void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        static void OnUndoRedo() => AssetTagsProjectBrowserToolbar.InvalidateSettingsCache();

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
                AssetTagsProjectBrowserToolbar.InvalidateSettingsCache();

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope())
            {
                if (GUILayout.Button("Sync Current Snapshot To Local JSON"))
                    AssetTagsManager.Instance.SyncCurrentSnapshotToLocal();

                if (GUILayout.Button("Convert All Asset Tags To Asset Labels"))
                    RunConvertTagsToAssetLabelsIfConfirmed();

                if (GUILayout.Button("Convert All Asset Labels To Asset Tags"))
                    RunConvertAssetLabelsToTagsIfConfirmed();
            }
        }
    }
}
