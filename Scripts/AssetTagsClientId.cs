#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public static class AssetTagsClientId
    {
        public const int ShortIdLength = 8;
        public const string DefaultClientId = "Default";

        [Serializable]
        public class Document
        {
            public string clientId;

            public string clientGuid;

            public string workstationId;
            public string workstationGuid;
        }

        static string ProjectRootFull =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static string UnityLibraryFull =>
            Path.GetFullPath(Path.Combine(ProjectRootFull, "Library"));

        static string GlobalDataDirFull =>
            Path.Combine(UnityLibraryFull, "INDiEA", "Asset Tags", "Data");

        public static string ClientJsonFullPath =>
            Path.Combine(GlobalDataDirFull, "ClientId.json");

        static string LegacyClientJsonAtProjectRootFull =>
            Path.Combine(ProjectRootFull, "ClientId.json");

        static string LegacyClientJsonAtLibraryRootFull =>
            Path.Combine(UnityLibraryFull, "ClientId.json");

        static void TryMigrateClientIdToGlobalDataDir()
        {
            try
            {
                if (File.Exists(ClientJsonFullPath))
                    return;
                var dir = Path.GetDirectoryName(ClientJsonFullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(LegacyClientJsonAtLibraryRootFull))
                {
                    File.Copy(LegacyClientJsonAtLibraryRootFull, ClientJsonFullPath, false);
                    return;
                }

                if (File.Exists(LegacyClientJsonAtProjectRootFull))
                {
                    File.Copy(LegacyClientJsonAtProjectRootFull, ClientJsonFullPath, false);
                    return;
                }
            }
            catch{}
        }

        static string LocalDataDirFull =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "INDiEA", "Asset Tags", "Data"));

        public static string GetOrCreateClientId()
        {
            TryMigrateClientIdToGlobalDataDir();
            var path = ClientJsonFullPath;
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var doc = JsonUtility.FromJson<Document>(json);
                    if (doc == null)
                        return CreateAndPersistNewShortId();

                    if (TryNormalizeShortId(doc.clientId, out var shortId))
                        return shortId;

                    if (TryNormalizeFullGuid(doc.clientGuid, out var full32))
                    {
                        var derived = full32.Substring(0, ShortIdLength);
                        WriteShortIdDocument(derived);
                        return derived;
                    }

                    if (TryNormalizeShortId(doc.clientGuid, out shortId))
                        return shortId;
                }
            }
            catch{}

            return CreateAndPersistNewShortId();
        }

        static string CreateAndPersistNewShortId()
        {
            for (var attempt = 0; attempt < 48; attempt++)
            {
                var candidate = RandomShortHex();
                if (LocalJsonPairExists(candidate))
                    continue;
                WriteShortIdDocument(candidate);
                return candidate;
            }

            var fallback = Guid.NewGuid().ToString("N").Substring(0, ShortIdLength);
            WriteShortIdDocument(fallback);
            return fallback;
        }

        static bool LocalJsonPairExists(string token)
        {
            if (!Directory.Exists(LocalDataDirFull))
                return false;
            var d = Path.Combine(LocalDataDirFull, $"AssetTagsData_{token}.json");
            var l = Path.Combine(LocalDataDirFull, $"AssetTagsList_{token}.json");
            return File.Exists(d) || File.Exists(l);
        }

        static string RandomShortHex()
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var u = BitConverter.ToUInt32(bytes, 0);
            return u.ToString("x8");
        }

        static void WriteShortIdDocument(string shortIdLower)
        {
            if (!TryNormalizeShortId(shortIdLower, out var id))
                id = Guid.NewGuid().ToString("N").Substring(0, ShortIdLength);
            var dir = Path.GetDirectoryName(ClientJsonFullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonUtility.ToJson(new Document { clientId = id }, true);
            File.WriteAllText(ClientJsonFullPath, json);
        }

        public static bool TryNormalizeShortId(string raw, out string idLower)
        {
            idLower = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var trimmed = raw.Trim();
            if (string.Equals(trimmed, DefaultClientId, StringComparison.OrdinalIgnoreCase))
            {
                idLower = DefaultClientId;
                return true;
            }
            var s = trimmed.ToLowerInvariant();
            if (s.Length != ShortIdLength)
                return false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c >= '0' && c <= '9' || c >= 'a' && c <= 'f')
                    continue;
                return false;
            }

            idLower = s;
            return true;
        }

        static bool TryNormalizeFullGuid(string raw, out string token32Lower)
        {
            token32Lower = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var s = raw.Trim().Replace("-", string.Empty).ToLowerInvariant();
            if (s.Length != 32)
                return false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c >= '0' && c <= '9' || c >= 'a' && c <= 'f')
                    continue;
                return false;
            }

            token32Lower = s;
            return true;
        }
    }
}
#endif
