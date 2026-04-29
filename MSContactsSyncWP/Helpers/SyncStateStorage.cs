// Helpers/SyncStateStorage.cs
// WP8.1 Silverlight: uses IsolatedStorageSettings.ApplicationSettings

using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;

namespace MSContactsSyncWP.Helpers
{
    public static class SyncStateStorage
    {
        private static IsolatedStorageSettings Settings =>
            IsolatedStorageSettings.ApplicationSettings;

        private const string Key = "MsSyncStateV1";

        private static void Set(string key, string value)
        {
            if (Settings.Contains(key)) Settings[key] = value;
            else                        Settings.Add(key, value);
            Settings.Save();
        }

        private static string Get(string key)
        {
            object v;
            return Settings.TryGetValue(key, out v) ? v as string : null;
        }

        // Format: id\tetag\n per contact
        public static void Save(Dictionary<string, string> etags)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in etags)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    sb.Append(Esc(kv.Key));   sb.Append('\t');
                    sb.Append(Esc(kv.Value)); sb.Append('\n');
                }
                Set(Key, sb.ToString());
            }
            catch { }
        }

        public static Dictionary<string, string> Load()
        {
            var result = new Dictionary<string, string>();
            try
            {
                string raw = Get(Key);
                if (string.IsNullOrEmpty(raw)) return result;
                foreach (string line in raw.Split('\n'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    int sep = line.IndexOf('\t');
                    if (sep < 0) continue;
                    string id   = Unesc(line.Substring(0, sep));
                    string etag = Unesc(line.Substring(sep + 1));
                    if (!string.IsNullOrEmpty(id)) result[id] = etag;
                }
            }
            catch { }
            return result;
        }

        public static void Clear()
        {
            if (Settings.Contains(Key)) Settings.Remove(Key);
            Settings.Save();
        }

        // DeltaLink for incremental sync
        public static void   SaveDeltaLink(string delta) => Set("MsDeltaLink", delta ?? "");
        public static string LoadDeltaLink()             => Get("MsDeltaLink");
        public static void   ClearDeltaLink()
        {
            if (Settings.Contains("MsDeltaLink")) Settings.Remove("MsDeltaLink");
            Settings.Save();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\\", "\\");
        }
    }
}
