using UnityEditor;
using UnityEngine;

namespace INDiEA.AssetTags
{
    [CustomEditor(typeof(AssetTagsSettings))]
    sealed class AssetTagsSettingsEditor : Editor
    {
        const string ConversionMenuRoot = "Window/INDiEA/Asset Tags/";
        SerializedProperty overrideProjectBrowserToolbar;
        SerializedProperty indexingSearchAfterTagChanges;
        SerializedProperty mergeDeletedTagRecords;
        SerializedProperty enableDebugLogs;

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

        static void RunClearCurrentLocalDataIfConfirmed()
        {
            if (!EditorUtility.DisplayDialog(
                    "Clear Current Local Data",
                    "Hide all currently known Asset Tags for this client, then clear this client's local tag assignments and tag list entries. Other client JSON files are not modified. Continue?",
                    "Clear",
                    "Cancel"))
                return;
            AssetTagsManager.Instance.ClearCurrentLocalData();
        }

        void OnEnable()
        {
            overrideProjectBrowserToolbar = serializedObject.FindProperty("overrideProjectBrowserToolbar");
            indexingSearchAfterTagChanges = serializedObject.FindProperty("indexingSearchAfterTagChanges");
            mergeDeletedTagRecords = serializedObject.FindProperty("mergeDeletedTagRecords");
            enableDebugLogs = serializedObject.FindProperty("enableDebugLogs");
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        static void OnUndoRedo()
        {
            AssetTagsProjectBrowserToolbar.InvalidateSettingsCache();
            AssetTagsSearchReindexCoordinator.InvalidateSettingsCache();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(overrideProjectBrowserToolbar);
            EditorGUILayout.PropertyField(indexingSearchAfterTagChanges);
            EditorGUILayout.PropertyField(mergeDeletedTagRecords);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                AssetTagsProjectBrowserToolbar.InvalidateSettingsCache();
                AssetTagsSearchReindexCoordinator.InvalidateSettingsCache();
            }
            else
            {
                serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope())
            {
                if (GUILayout.Button("Save Current Snapshot to Local Data"))
                    AssetTagsManager.Instance.SaveCurrentTagsToLocalData();

                if (GUILayout.Button("Clear Current Local Data"))
                    RunClearCurrentLocalDataIfConfirmed();

                if (GUILayout.Button("Convert All Asset Tags To Asset Labels"))
                    RunConvertTagsToAssetLabelsIfConfirmed();

                if (GUILayout.Button("Convert All Asset Labels To Asset Tags"))
                    RunConvertAssetLabelsToTagsIfConfirmed();
            }

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(enableDebugLogs);
            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();
        }
    }
}
