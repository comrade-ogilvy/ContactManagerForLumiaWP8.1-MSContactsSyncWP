// Services/ContactStoreService.cs
// WP8.1 Silverlight — Windows.Phone.PersonalInformation API
//
// Verified KnownContactProperties (full list from MSDN):
//   AdditionalName, Address, AlternateMobileTelephone, AlternateTelephone,
//   AlternateWorkTelephone, Anniversary, Birthdate, Children, CompanyName,
//   CompanyTelephone, DisplayName, Email, FamilyName, GivenName, HomeFax,
//   HonorificPrefix, HonorificSuffix, JobTitle, Manager, MobileTelephone,
//   Nickname, Notes, OfficeLocation, OtherAddress, OtherEmail,
//   SignificantOther, Telephone, Url, WorkAddress, WorkEmail, WorkFax,
//   WorkTelephone, YomiCompanyName, YomiFamilyName, YomiGivenName
//
// NOT in the API (removed vs previous attempt):
//   Department, WorkTelephone2, HomeTelephone, HomeTelephone2,
//   Email2, Email3, HomeAddress
//
// Enumerate all contacts: store.CreateContactQuery().GetContactsAsync()
//   NOT GetContactQuery() — that doesn't exist.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Phone.PersonalInformation;
using MSContactsSyncWP.Models;

namespace MSContactsSyncWP.Services
{
    public class ContactStoreService
    {
        private ContactStore _store;
        private const string RemoteIdPrefix = "MSContactsSync_";

        private async Task<ContactStore> GetStoreAsync()
        {
            if (_store != null) return _store;
            _store = await ContactStore.CreateOrOpenAsync(
                ContactStoreSystemAccessMode.ReadWrite,
                ContactStoreApplicationAccessMode.ReadOnly);
            return _store;
        }

        // ================================================================
        // UPSERT — save or update contact
        // ================================================================
        public async Task UpsertContactAsync(MsContact mc)
        {
            try
            {
                var store = await GetStoreAsync();
                string remoteId = RemoteIdPrefix + mc.Id;

                StoredContact contact = await store.FindContactByRemoteIdAsync(remoteId);
                if (contact == null)
                {
                    contact = new StoredContact(store);
                    contact.RemoteId = remoteId;
                }

                // ---- Name ----
                contact.GivenName = mc.FirstName ?? "";
                contact.FamilyName = mc.LastName ?? "";

                if (string.IsNullOrEmpty(contact.GivenName) &&
                    string.IsNullOrEmpty(contact.FamilyName) &&
                    !string.IsNullOrEmpty(mc.DisplayName))
                    contact.DisplayName = mc.DisplayName;


                // ---- Extended properties ----
                var props = await contact.GetPropertiesAsync();

                SetProp(props, KnownContactProperties.AdditionalName, mc.MiddleName);
                SetProp(props, KnownContactProperties.CompanyName, mc.Company);
                SetProp(props, KnownContactProperties.JobTitle, mc.JobTitle);
                // Department has no KnownContactProperties slot — store in OfficeLocation
                // as the closest available field
                SetProp(props, KnownContactProperties.OfficeLocation, mc.Department);
                SetProp(props, KnownContactProperties.Notes, mc.Notes);
                SetProp(props, KnownContactProperties.Nickname, mc.Nickname);

                // ---- Phones ----
                // Mobile
                if (!string.IsNullOrEmpty(mc.MobilePhone))
                    SetProp(props, KnownContactProperties.MobileTelephone, mc.MobilePhone);

                // Work phones — WorkTelephone + AlternateWorkTelephone
                if (mc.BusinessPhones != null && mc.BusinessPhones.Count > 0)
                {
                    SetProp(props, KnownContactProperties.WorkTelephone, mc.BusinessPhones[0]);
                    if (mc.BusinessPhones.Count > 1)
                        SetProp(props, KnownContactProperties.AlternateWorkTelephone, mc.BusinessPhones[1]);
                }

                // Home phones — Telephone + AlternateTelephone
                if (mc.HomePhones != null && mc.HomePhones.Count > 0)
                {
                    SetProp(props, KnownContactProperties.Telephone, mc.HomePhones[0]);
                    if (mc.HomePhones.Count > 1)
                        SetProp(props, KnownContactProperties.AlternateTelephone, mc.HomePhones[1]);
                }

                // ---- Emails — Email + WorkEmail + OtherEmail ----
                if (mc.Emails != null)
                {
                    SetProp(props, KnownContactProperties.Email,
                        mc.Emails.Count > 0 ? mc.Emails[0].Address : null);
                    SetProp(props, KnownContactProperties.WorkEmail,
                        mc.Emails.Count > 1 ? mc.Emails[1].Address : null);
                    SetProp(props, KnownContactProperties.OtherEmail,
                        mc.Emails.Count > 2 ? mc.Emails[2].Address : null);
                }

                // ---- Addresses ----
                // KnownContactProperties has: WorkAddress, OtherAddress, Address
                // There is no HomeAddress — map home→Address, work→WorkAddress, other→OtherAddress
                if (mc.Addresses != null)
                {
                    foreach (var a in mc.Addresses)
                    {
                        var ca = new ContactAddress
                        {
                            StreetAddress = a.Street ?? "",
                            Locality = a.City ?? "",
                            Region = a.State ?? "",
                            PostalCode = a.PostalCode ?? "",
                            Country = a.CountryOrRegion ?? ""
                        };

                        switch (a.Type)
                        {
                            case "work":
                                props[KnownContactProperties.WorkAddress] = ca; break;
                            case "home":
                                props[KnownContactProperties.Address] = ca; break;
                            default:
                                props[KnownContactProperties.OtherAddress] = ca; break;
                        }
                    }
                }

                // ---- Birthday ----
                if (!string.IsNullOrEmpty(mc.Birthday))
                {
                    DateTime bday;
                    if (DateTime.TryParse(mc.Birthday, out bday))
                        props[KnownContactProperties.Birthdate] = bday;
                }

                await contact.SaveAsync();
            }
            catch { }
        }

        // ================================================================
        // DELETE contact by Graph ID
        // ================================================================
        public async Task DeleteContactAsync(string graphId)
        {
            try
            {
                var store = await GetStoreAsync();
                string remoteId = RemoteIdPrefix + graphId;
                var contact = await store.FindContactByRemoteIdAsync(remoteId);
                if (contact != null)
                    await store.DeleteContactAsync(contact.Id);
            }
            catch { }
        }

        // ================================================================
        // DELETE all app contacts
        // ================================================================
        public async Task DeleteAllContactsAsync(Action<string> progress = null)
        {
            try
            {
                var store = await GetStoreAsync();
                // Correct API: CreateContactQuery() → GetContactsAsync()
                var result = store.CreateContactQuery();
                var contacts = await result.GetContactsAsync();
                int deleted = 0;
                foreach (var c in contacts)
                {
                    if (c.RemoteId != null && c.RemoteId.StartsWith(RemoteIdPrefix))
                    {
                        await store.DeleteContactAsync(c.Id);
                        deleted++;
                        if (progress != null && deleted % 10 == 0)
                            progress("Deleted " + deleted + "...");
                    }
                }
            }
            catch { }
        }

        // ================================================================
        // HELPER — set or remove a property
        // ================================================================
        private static void SetProp(IDictionary<string, object> props,
            string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
                props[key] = value;
            else if (props.ContainsKey(key))
                props.Remove(key);
        }
    }
}