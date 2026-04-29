// Helpers/CredentialStorage.cs
// WP8.1 Silverlight: uses IsolatedStorageSettings.ApplicationSettings
// instead of Windows.Storage.ApplicationData.Current.LocalSettings

using System.IO.IsolatedStorage;

namespace MSContactsSyncWP.Helpers
{
    public static class CredentialStorage
    {
        private static IsolatedStorageSettings Settings =>
            IsolatedStorageSettings.ApplicationSettings;

        // ---- helpers -------------------------------------------------------
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

        private static void Remove(string key)
        {
            if (Settings.Contains(key)) Settings.Remove(key);
            Settings.Save();
        }

        // ---- public API (same as UWP version) ------------------------------
        public static void   SaveClientId(string id)    => Set("ClientId", id);
        public static string LoadClientId()             => Get("ClientId");

        public static void   SaveToken(string rt)       => Set("RefreshToken", rt);
        public static string LoadToken()                => Get("RefreshToken");
        public static bool   HasToken()                 => !string.IsNullOrEmpty(LoadToken());
        public static void   DeleteToken()              { Remove("RefreshToken"); Remove("AccessToken"); }

        public static void   SaveAccessToken(string at) => Set("AccessToken", at);
        public static string LoadAccessToken()          => Get("AccessToken");

        public static void SaveExpiry(long expiresOn)   => Set("TokenExpiry", expiresOn.ToString());
        public static long LoadExpiry()
        {
            string s = Get("TokenExpiry");
            long v; long.TryParse(s, out v); return v;
        }

        public static void   SaveUsername(string u)     => Set("Username", u ?? "");
        public static string LoadUsername()             => Get("Username") ?? "";
    }
}
