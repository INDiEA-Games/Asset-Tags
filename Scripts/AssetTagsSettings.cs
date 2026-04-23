using UnityEngine;
using UnityEngine.Serialization;

namespace INDiEA.AssetTags
{
    public sealed class AssetTagsSettings : ScriptableObject
    {
        [SerializeField]
        [FormerlySerializedAs("showProjectBrowserToolbar")]
        bool overrideProjectBrowserToolbar = true;
        [SerializeField]
        bool enableDiagnosticLogs = true;

        public bool OverrideProjectBrowserToolbar => overrideProjectBrowserToolbar;
        public bool EnableDiagnosticLogs => enableDiagnosticLogs;
    }
}
