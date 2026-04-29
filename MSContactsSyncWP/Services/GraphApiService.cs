// Services/GraphApiService.cs
// WP8.1 Silverlight — uses HttpWebRequest only (always available, no NuGet needed).
// System.Net.Http is NOT used anywhere in this file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MSContactsSyncWP.Helpers;
using MSContactsSyncWP.Models;

namespace MSContactsSyncWP.Services
{
    public class GraphApiService
    {
        private const string GraphBase     = "https://graph.microsoft.com/v1.0";
        private const string TokenUrl      = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";

        public const string CacheFileName = "msal_contacts_token_cache.json";

        private readonly string _clientId;

        public string DeviceCode      { get; private set; }
        public string UserCode        { get; private set; }
        public string VerificationUrl { get; private set; }
        public int    ExpiresIn       { get; private set; }
        public int    Interval        { get; private set; }

        public GraphApiService(string clientId)
        {
            _clientId = clientId;
        }

        // ================================================================
        // DEVICE FLOW — Step 1
        // ================================================================
        public async Task<bool> StartDeviceFlowAsync()
        {
            try
            {
                string body =
                    "client_id=" + Uri.EscapeDataString(_clientId) +
                    "&scope=" + Uri.EscapeDataString(
                        "offline_access User.Read Contacts.Read");

                string json = await PostAsync(DeviceCodeUrl, body);
                if (string.IsNullOrEmpty(json)) return false;

                DeviceCode      = JsonHelper.ParseTokenValue(json, "device_code");
                UserCode        = JsonHelper.ParseTokenValue(json, "user_code");
                VerificationUrl = JsonHelper.ParseTokenValue(json, "verification_uri");

                string exp = JsonHelper.ParseTokenValue(json, "expires_in");
                string itv = JsonHelper.ParseTokenValue(json, "interval");
                int e = 0; int.TryParse(exp, out e); ExpiresIn = e;
                int i = 5; int.TryParse(itv, out i); Interval  = Math.Max(5, i);

                return !string.IsNullOrEmpty(UserCode);
            }
            catch { return false; }
        }

        // ================================================================
        // DEVICE FLOW — Step 2: poll
        // ================================================================
        public async Task<string> PollForTokenAsync()
        {
            try
            {
                string body =
                    "grant_type=urn:ietf:params:oauth:grant-type:device_code" +
                    "&client_id=" + Uri.EscapeDataString(_clientId) +
                    "&device_code=" + Uri.EscapeDataString(DeviceCode);

                string json = await PostAsync(TokenUrl, body);
                if (string.IsNullOrEmpty(json)) return "error";

                string error       = JsonHelper.ParseTokenValue(json, "error");
                string accessToken = JsonHelper.ParseTokenValue(json, "access_token");
                string refreshTok  = JsonHelper.ParseTokenValue(json, "refresh_token");

                if (!string.IsNullOrEmpty(accessToken))
                {
                    CredentialStorage.SaveAccessToken(accessToken);
                    if (!string.IsNullOrEmpty(refreshTok))
                        CredentialStorage.SaveToken(refreshTok);
                    string ei = JsonHelper.ParseTokenValue(json, "expires_in");
                    int eiVal = 3600; int.TryParse(ei, out eiVal);
                    CredentialStorage.SaveExpiry(EpochNow() + eiVal);
                    return "ok";
                }
                return error ?? "error";
            }
            catch { return "error"; }
        }

        // ================================================================
        // REFRESH access token
        // ================================================================
        public async Task<string> GetAccessTokenAsync(string refreshToken)
        {
            try
            {
                string body =
                    "grant_type=refresh_token" +
                    "&client_id=" + Uri.EscapeDataString(_clientId) +
                    "&refresh_token=" + Uri.EscapeDataString(refreshToken) +
                    "&scope=" + Uri.EscapeDataString(
                        "offline_access User.Read Contacts.Read");

                string json = await PostAsync(TokenUrl, body);
                if (string.IsNullOrEmpty(json)) return null;

                string accessToken = JsonHelper.ParseTokenValue(json, "access_token");
                string newRefresh  = JsonHelper.ParseTokenValue(json, "refresh_token");

                if (!string.IsNullOrEmpty(accessToken))
                {
                    CredentialStorage.SaveAccessToken(accessToken);
                    if (!string.IsNullOrEmpty(newRefresh))
                        CredentialStorage.SaveToken(newRefresh);
                    string ei = JsonHelper.ParseTokenValue(json, "expires_in");
                    int eiVal = 3600; int.TryParse(ei, out eiVal);
                    CredentialStorage.SaveExpiry(EpochNow() + eiVal);
                    return accessToken;
                }
                return null;
            }
            catch { return null; }
        }

        // ================================================================
        // GET username
        // ================================================================
        public async Task<string> GetUsernameAsync(string accessToken)
        {
            try
            {
                string json = await GetAsync(GraphBase + "/me", accessToken);
                if (string.IsNullOrEmpty(json)) return null;
                string upn  = JsonHelper.GetString(json, "userPrincipalName");
                string mail = JsonHelper.GetString(json, "mail");
                return !string.IsNullOrEmpty(upn) ? upn : mail;
            }
            catch { return null; }
        }

        // ================================================================
        // FETCH all contacts
        // ================================================================
        public async Task<ContactsFetchResult> FetchAllContactsAsync(
            string accessToken, Action<string> progress = null)
        {
            var all   = new List<MsContact>();
            string delta = null;

            string fields =
                "id,displayName,givenName,middleName,surname,nickName," +
                "companyName,department,jobTitle,personalNotes,mobilePhone," +
                "businessPhones,homePhones,emailAddresses,businessAddress," +
                "homeAddress,otherAddress,birthday";

            string savedDelta = SyncStateStorage.LoadDeltaLink();
            string url = !string.IsNullOrEmpty(savedDelta)
                ? savedDelta
                : GraphBase + "/me/contacts/delta?$select=" + fields;

            int page = 0;
            while (!string.IsNullOrEmpty(url))
            {
                page++;
                if (progress != null) progress("Page " + page + "...");
                string json = await GetAsync(url, accessToken);
                if (string.IsNullOrEmpty(json)) break;
                var list = JsonHelper.ParseContacts(json);
                foreach (var c in list) c.FolderPath = "Default";
                all.AddRange(list);
                delta = JsonHelper.ParseDeltaLink(json);
                url   = JsonHelper.ParseNextLink(json);
            }

            // Folders
            string fj = await GetAsync(
                GraphBase + "/me/contactFolders?$top=100", accessToken);
            if (!string.IsNullOrEmpty(fj))
            {
                var folders = JsonHelper.ParseFolders(fj);
                foreach (var folder in folders)
                {
                    if (progress != null) progress("Folder: " + folder.Name);
                    string fu = GraphBase +
                        "/me/contactFolders/" + folder.Id +
                        "/contacts?$top=100&$select=" + fields;
                    while (!string.IsNullOrEmpty(fu))
                    {
                        string fcontacts = await GetAsync(fu, accessToken);
                        if (string.IsNullOrEmpty(fcontacts)) break;
                        var fc = JsonHelper.ParseContacts(fcontacts);
                        foreach (var c in fc) c.FolderPath = folder.Name;
                        all.AddRange(fc);
                        fu = JsonHelper.ParseNextLink(fcontacts);
                    }
                }
            }

            return new ContactsFetchResult { Contacts = all, DeltaLink = delta };
        }

        // ================================================================
        // BUILD MSAL cache JSON
        // ================================================================
        public string BuildMsalCacheJson(string clientId, string refreshToken,
            string accessToken, string username, long expiresOn)
        {
            long   now    = EpochNow();
            string homeId = "00000000-0000-0000-0000-000000000000." +
                            "9188040d-6c67-4c5b-b112-36a304b66dad";
            string env    = "login.microsoftonline.com";
            string realm  = "consumers";
            string target = "Contacts.Read User.Read openid profile";

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"AccessToken\": {");
            sb.Append("    \"").Append(homeId).Append("-").Append(env)
              .Append("-accesstoken-").Append(clientId).Append("-").Append(realm)
              .Append("-contacts.read user.read openid profile\": {");
            sb.AppendLine();
            sb.AppendLine("      \"credential_type\": \"AccessToken\",");
            sb.Append("      \"secret\": \"").Append(EscJson(accessToken ?? "")).AppendLine("\",");
            sb.Append("      \"home_account_id\": \"").Append(homeId).AppendLine("\",");
            sb.Append("      \"environment\": \"").Append(env).AppendLine("\",");
            sb.Append("      \"client_id\": \"").Append(clientId).AppendLine("\",");
            sb.Append("      \"target\": \"").Append(target).AppendLine("\",");
            sb.Append("      \"realm\": \"").Append(realm).AppendLine("\",");
            sb.AppendLine("      \"token_type\": \"Bearer\",");
            sb.Append("      \"cached_at\": \"").Append(now).AppendLine("\",");
            sb.Append("      \"expires_on\": \"").Append(expiresOn).AppendLine("\",");
            sb.Append("      \"extended_expires_on\": \"").Append(expiresOn).AppendLine("\"");
            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"Account\": {");
            sb.Append("    \"").Append(homeId).Append("-").Append(env)
              .Append("-").Append(realm).AppendLine("\": {");
            sb.Append("      \"home_account_id\": \"").Append(homeId).AppendLine("\",");
            sb.Append("      \"environment\": \"").Append(env).AppendLine("\",");
            sb.Append("      \"realm\": \"").Append(realm).AppendLine("\",");
            sb.Append("      \"username\": \"").Append(EscJson(username ?? "")).AppendLine("\",");
            sb.AppendLine("      \"authority_type\": \"MSSTS\",");
            sb.AppendLine("      \"account_source\": \"urn:ietf:params:oauth:grant-type:device_code\"");
            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"RefreshToken\": {");
            sb.Append("    \"").Append(homeId).Append("-").Append(env)
              .Append("-refreshtoken-").Append(clientId)
              .Append("--contacts.read user.read openid profile\": {");
            sb.AppendLine();
            sb.AppendLine("      \"credential_type\": \"RefreshToken\",");
            sb.Append("      \"secret\": \"").Append(EscJson(refreshToken ?? "")).AppendLine("\",");
            sb.Append("      \"home_account_id\": \"").Append(homeId).AppendLine("\",");
            sb.Append("      \"environment\": \"").Append(env).AppendLine("\",");
            sb.Append("      \"client_id\": \"").Append(clientId).AppendLine("\",");
            sb.Append("      \"target\": \"").Append(target).AppendLine("\",");
            sb.Append("      \"last_modification_time\": \"").Append(now).AppendLine("\"");
            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"AppMetadata\": {");
            sb.Append("    \"appmetadata-").Append(env).Append("-")
              .Append(clientId).AppendLine("\": {");
            sb.Append("      \"client_id\": \"").Append(clientId).AppendLine("\",");
            sb.Append("      \"environment\": \"").Append(env).AppendLine("\"");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ================================================================
        // HTTP POST — form-urlencoded, returns response body string
        // ================================================================
        private async Task<string> PostAsync(string url, string formBody)
        {
            try
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(formBody);
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method      = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.Accept      = "application/json";

                using (var stream = await Task.Factory.FromAsync(
                    req.BeginGetRequestStream, req.EndGetRequestStream, null))
                {
                    await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                }

                return await ReadResponseAsync(req);
            }
            catch { return null; }
        }

        // ================================================================
        // HTTP GET with Bearer token and retry
        // ================================================================
        private async Task<string> GetAsync(string url, string accessToken)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Accept = "application/json";
                    req.Headers[HttpRequestHeader.Authorization] =
                        "Bearer " + accessToken;

                    string body = await ReadResponseAsync(req);
                    if (body != null) return body;
                }
                catch (WebException ex)
                {
                    int code = 0;
                    var httpResp = ex.Response as HttpWebResponse;
                    if (httpResp != null) code = (int)httpResp.StatusCode;
                    if (code == 429 || code >= 500)
                    {
                        int delaySec = (int)Math.Min(60, Math.Pow(2, attempt + 1));
                        await Task.Delay(delaySec * 1000);
                        continue;
                    }
                    return null;
                }
                catch { return null; }
            }
            return null;
        }

        // ================================================================
        // Read response body, including error bodies on 4xx/5xx
        // ================================================================
        private async Task<string> ReadResponseAsync(HttpWebRequest req)
        {
            try
            {
                var resp = (HttpWebResponse)await Task.Factory.FromAsync(
                    req.BeginGetResponse, req.EndGetResponse, null);
                using (resp)
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    return await reader.ReadToEndAsync();
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                    using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                        return await reader.ReadToEndAsync();
                return null;
            }
        }

        // ================================================================
        // HELPERS
        // ================================================================
        private static long EpochNow() =>
            (long)(DateTime.UtcNow -
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    public class ContactsFetchResult
    {
        public List<MsContact> Contacts  { get; set; }
        public string          DeltaLink { get; set; }
    }
}
