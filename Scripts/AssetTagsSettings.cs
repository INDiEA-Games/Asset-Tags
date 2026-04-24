using UnityEngine;

namespace INDiEA.AssetTags
{
    public sealed class AssetTagsSettings : ScriptableObject
    {
        [SerializeField]
        bool overrideProjectBrowserToolbar = true;
        [SerializeField]
        bool indexingSearchAfterTagChanges = true;
        [SerializeField]
        bool mergeDeletedTagRecords = true;
        [SerializeField]
        bool enableDebugLogs = true;

        public bool OverrideProjectBrowserToolbar => overrideProjectBrowserToolbar;
        public bool IndexingSearchAfterTagChanges => indexingSearchAfterTagChanges;
        public bool MergeDeletedTagRecords => mergeDeletedTagRecords;
        public bool EnableDebugLogs => enableDebugLogs;
    }
}
