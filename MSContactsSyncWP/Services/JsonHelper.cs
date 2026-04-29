// Services/JsonHelper.cs
// WP8.1 Silverlight: Windows.Data.Json is NOT available.
// We use a minimal hand-rolled JSON parser sufficient for the MS Graph
// responses this app deals with.  For production use you could swap in
// Newtonsoft.Json (NuGet: Newtonsoft.Json) and replace the helpers below.

using System;
using System.Collections.Generic;
using MSContactsSyncWP.Models;

namespace MSContactsSyncWP.Services
{
    public class FolderInfo
    {
        public string Id   { get; set; }
        public string Name { get; set; }
    }

    public class MsalCacheData
    {
        public string ClientId     { get; set; }
        public string RefreshToken { get; set; }
        public string AccessToken  { get; set; }
        public long   ExpiresOn    { get; set; }
        public string Username     { get; set; }

        public bool IsValid =>
            !string.IsNullOrEmpty(RefreshToken) &&
            !string.IsNullOrEmpty(ClientId);

        public bool AccessTokenValid
        {
            get
            {
                if (string.IsNullOrEmpty(AccessToken)) return false;
                return ExpiresOn > EpochNow() + 60;
            }
        }

        private static long EpochNow() =>
            (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }

    // ======================================================================
    // Tiny JSON helpers — covers the subset of MS Graph responses we need.
    // Limitations:
    //   • No nested-object lookup beyond one level (use nested calls).
    //   • String values only (numbers returned as strings).
    //   • No unicode \uXXXX un-escaping beyond what C# string literals give us.
    // ======================================================================
    public static class JsonHelper
    {
        // ----------------------------------------------------------------
        // PUBLIC: parse contacts from /me/contacts response
        // ----------------------------------------------------------------
        public static List<MsContact> ParseContacts(string json)
        {
            var list = new List<MsContact>();
            try
            {
                string arrJson = ExtractArrayJson(json, "value");
                if (string.IsNullOrEmpty(arrJson)) return list;
                foreach (string item in SplitTopLevelObjects(arrJson))
                {
                    var c = ParseContact(item);
                    if (c != null) list.Add(c);
                }
            }
            catch { }
            return list;
        }

        public static MsContact ParseContact(string obj)
        {
            try
            {
                var c = new MsContact();
                c.Id          = GetString(obj, "id");
                c.ETag        = GetString(obj, "@odata.etag");
                c.IsDeleted   = obj.Contains("\"@removed\"");
                c.DisplayName = GetString(obj, "displayName");
                c.FirstName   = GetString(obj, "givenName");
                c.MiddleName  = GetString(obj, "middleName");
                c.LastName    = GetString(obj, "surname");
                c.Nickname    = GetString(obj, "nickName");
                c.Company     = GetString(obj, "companyName");
                c.Department  = GetString(obj, "department");
                c.JobTitle    = GetString(obj, "jobTitle");
                c.Notes       = GetString(obj, "personalNotes");
                c.MobilePhone = GetString(obj, "mobilePhone");

                string bday = GetString(obj, "birthday");
                if (!string.IsNullOrEmpty(bday) && bday.Contains("T"))
                    bday = bday.Split('T')[0];
                c.Birthday = bday;

                // businessPhones
                string bpJson = ExtractArrayJson(obj, "businessPhones");
                if (!string.IsNullOrEmpty(bpJson))
                    foreach (string s in SplitStringArray(bpJson))
                        if (!string.IsNullOrEmpty(s)) c.BusinessPhones.Add(s);

                // homePhones
                string hpJson = ExtractArrayJson(obj, "homePhones");
                if (!string.IsNullOrEmpty(hpJson))
                    foreach (string s in SplitStringArray(hpJson))
                        if (!string.IsNullOrEmpty(s)) c.HomePhones.Add(s);

                // emailAddresses
                string emJson = ExtractArrayJson(obj, "emailAddresses");
                if (!string.IsNullOrEmpty(emJson))
                    foreach (string item in SplitTopLevelObjects(emJson))
                    {
                        string addr = GetString(item, "address");
                        if (!string.IsNullOrEmpty(addr))
                            c.Emails.Add(new MsEmail
                            {
                                Name    = GetString(item, "name"),
                                Address = addr
                            });
                    }

                // addresses
                ParseAddress(obj, "businessAddress", "work",  c.Addresses);
                ParseAddress(obj, "homeAddress",     "home",  c.Addresses);
                ParseAddress(obj, "otherAddress",    "other", c.Addresses);

                if (string.IsNullOrEmpty(c.Id)) return null;
                return c;
            }
            catch { return null; }
        }

        private static void ParseAddress(string obj, string key,
            string type, List<MsAddress> list)
        {
            string ao = ExtractObjectJson(obj, key);
            if (string.IsNullOrEmpty(ao)) return;
            string street  = GetString(ao, "street");
            string city    = GetString(ao, "city");
            string state   = GetString(ao, "state");
            string postal  = GetString(ao, "postalCode");
            string country = GetString(ao, "countryOrRegion");
            if (string.IsNullOrEmpty(street) && string.IsNullOrEmpty(city) &&
                string.IsNullOrEmpty(postal)) return;
            list.Add(new MsAddress
            {
                Type            = type,
                Street          = street,
                City            = city,
                State           = state,
                PostalCode      = postal,
                CountryOrRegion = country
            });
        }

        // ----------------------------------------------------------------
        // Paging links
        // ----------------------------------------------------------------
        public static string ParseNextLink(string json)  => GetString(json, "@odata.nextLink");
        public static string ParseDeltaLink(string json) => GetString(json, "@odata.deltaLink");

        // ----------------------------------------------------------------
        // Contact folders
        // ----------------------------------------------------------------
        public static List<FolderInfo> ParseFolders(string json)
        {
            var list = new List<FolderInfo>();
            try
            {
                string arrJson = ExtractArrayJson(json, "value");
                if (string.IsNullOrEmpty(arrJson)) return list;
                foreach (string item in SplitTopLevelObjects(arrJson))
                {
                    string id   = GetString(item, "id");
                    string name = GetString(item, "displayName");
                    if (!string.IsNullOrEmpty(id))
                        list.Add(new FolderInfo { Id = id, Name = name });
                }
            }
            catch { }
            return list;
        }

        // ----------------------------------------------------------------
        // Token response
        // ----------------------------------------------------------------
        public static string ParseTokenValue(string json, string key) => GetString(json, key);

        // ----------------------------------------------------------------
        // MSAL cache (same structure as UWP version)
        // ----------------------------------------------------------------
        public static MsalCacheData ParseMsalCache(string json)
        {
            var result = new MsalCacheData();
            try
            {
                // AppMetadata → client_id
                string meta = ExtractObjectJson(json, "AppMetadata");
                if (!string.IsNullOrEmpty(meta))
                {
                    foreach (string entry in SplitTopLevelObjects(meta))
                    {
                        string cid = GetString(entry, "client_id");
                        if (!string.IsNullOrEmpty(cid)) { result.ClientId = cid; break; }
                    }
                }

                // RefreshToken → secret
                string rt = ExtractObjectJson(json, "RefreshToken");
                if (!string.IsNullOrEmpty(rt))
                    foreach (string entry in SplitTopLevelObjects(rt))
                    {
                        string s = GetString(entry, "secret");
                        if (!string.IsNullOrEmpty(s)) { result.RefreshToken = s; break; }
                    }

                // AccessToken → secret + expires_on
                string at = ExtractObjectJson(json, "AccessToken");
                if (!string.IsNullOrEmpty(at))
                    foreach (string entry in SplitTopLevelObjects(at))
                    {
                        string s   = GetString(entry, "secret");
                        string exp = GetString(entry, "expires_on");
                        if (!string.IsNullOrEmpty(s))
                        {
                            result.AccessToken = s;
                            long v; long.TryParse(exp, out v);
                            result.ExpiresOn = v;
                            break;
                        }
                    }

                // Account → username
                string acc = ExtractObjectJson(json, "Account");
                if (!string.IsNullOrEmpty(acc))
                    foreach (string entry in SplitTopLevelObjects(acc))
                    {
                        string u = GetString(entry, "username");
                        if (!string.IsNullOrEmpty(u)) { result.Username = u; break; }
                    }
            }
            catch { }
            return result;
        }

        // ==================================================================
        // CORE HELPERS
        // ==================================================================

        /// <summary>
        /// Extracts the string value for a key from a flat JSON object string.
        /// Handles: "key":"value"  and  "key": "value" with escaped chars.
        /// Also handles numeric values (returns as string).
        /// </summary>
        public static string GetString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "";
            // Build search token: "key":
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search, StringComparison.Ordinal);
            if (ki < 0) return "";

            int pos = ki + search.Length;
            // skip whitespace and colon
            while (pos < json.Length && (json[pos] == ' ' || json[pos] == ':' || json[pos] == '\r' || json[pos] == '\n' || json[pos] == '\t'))
                pos++;
            if (pos >= json.Length) return "";

            if (json[pos] == '"')
            {
                // string value
                pos++; // skip opening quote
                var sb = new System.Text.StringBuilder();
                while (pos < json.Length && json[pos] != '"')
                {
                    if (json[pos] == '\\' && pos + 1 < json.Length)
                    {
                        pos++;
                        switch (json[pos])
                        {
                            case '"':  sb.Append('"');  break;
                            case '\\': sb.Append('\\'); break;
                            case '/':  sb.Append('/');  break;
                            case 'n':  sb.Append('\n'); break;
                            case 'r':  sb.Append('\r'); break;
                            case 't':  sb.Append('\t'); break;
                            default:   sb.Append(json[pos]); break;
                        }
                    }
                    else sb.Append(json[pos]);
                    pos++;
                }
                return sb.ToString();
            }
            else if (json[pos] == 'n' && pos + 3 < json.Length &&
                     json.Substring(pos, 4) == "null")
            {
                return "";
            }
            else if (json[pos] == 't' || json[pos] == 'f')
            {
                // boolean
                int end = pos;
                while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
                return json.Substring(pos, end - pos).Trim();
            }
            else
            {
                // number
                int end = pos;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']')
                    end++;
                return json.Substring(pos, end - pos).Trim();
            }
        }

        /// <summary>
        /// Extracts the raw JSON of a named array: "key": [ ... ]
        /// Returns everything INSIDE the brackets.
        /// </summary>
        private static string ExtractArrayJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search, StringComparison.Ordinal);
            if (ki < 0) return null;
            int pos = ki + search.Length;
            while (pos < json.Length && json[pos] != '[') pos++;
            if (pos >= json.Length) return null;
            return ExtractBracketed(json, pos, '[', ']');
        }

        /// <summary>
        /// Extracts the raw JSON of a named object: "key": { ... }
        /// Returns everything INCLUDING the braces.
        /// </summary>
        private static string ExtractObjectJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search, StringComparison.Ordinal);
            if (ki < 0) return null;
            int pos = ki + search.Length;
            while (pos < json.Length && json[pos] != '{') pos++;
            if (pos >= json.Length) return null;
            return ExtractBracketed(json, pos, '{', '}');
        }

        /// <summary>
        /// Returns the substring from open-bracket at startPos to matching
        /// close-bracket, respecting nesting and string literals.
        /// </summary>
        private static string ExtractBracketed(string json, int startPos,
            char open, char close)
        {
            int depth = 0;
            int start = startPos;
            bool inString = false;
            for (int i = startPos; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString)
                {
                    if (ch == '\\') { i++; continue; } // skip escaped char
                    if (ch == '"')  inString = false;
                    continue;
                }
                if (ch == '"') { inString = true; continue; }
                if (ch == open)  { depth++; if (depth == 1) start = i; }
                if (ch == close) { depth--; if (depth == 0) return json.Substring(start + 1, i - start - 1); }
            }
            return null;
        }

        /// <summary>
        /// Splits a bracket-stripped array body into top-level object strings.
        /// E.g. {"a":1},{"b":2}  →  [ "{\"a\":1}", "{\"b\":2}" ]
        /// </summary>
        private static List<string> SplitTopLevelObjects(string arrayBody)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(arrayBody)) return list;
            int i = 0;
            while (i < arrayBody.Length)
            {
                while (i < arrayBody.Length && arrayBody[i] != '{') i++;
                if (i >= arrayBody.Length) break;
                int start = i;
                int depth = 0;
                bool inStr = false;
                while (i < arrayBody.Length)
                {
                    char ch = arrayBody[i];
                    if (inStr) { if (ch == '\\') { i++; } else if (ch == '"') inStr = false; i++; continue; }
                    if (ch == '"') { inStr = true; i++; continue; }
                    if (ch == '{') depth++;
                    if (ch == '}') { depth--; if (depth == 0) { list.Add(arrayBody.Substring(start, i - start + 1)); i++; break; } }
                    i++;
                }
            }
            return list;
        }

        /// <summary>
        /// Splits a bracket-stripped array of strings: "a","b","c"
        /// </summary>
        private static List<string> SplitStringArray(string arrayBody)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(arrayBody)) return list;
            int i = 0;
            while (i < arrayBody.Length)
            {
                while (i < arrayBody.Length && arrayBody[i] != '"') i++;
                if (i >= arrayBody.Length) break;
                i++; // skip opening quote
                var sb = new System.Text.StringBuilder();
                while (i < arrayBody.Length && arrayBody[i] != '"')
                {
                    if (arrayBody[i] == '\\' && i + 1 < arrayBody.Length) { i++; sb.Append(arrayBody[i]); }
                    else sb.Append(arrayBody[i]);
                    i++;
                }
                list.Add(sb.ToString());
                i++; // skip closing quote
            }
            return list;
        }
    }
}
