// Models/MsContact.cs
using System.Collections.Generic;

namespace MSContactsSyncWP.Models
{
    public class MsContact
    {
        public string Id          { get; set; }
        public string ETag        { get; set; }
        public string DisplayName { get; set; }
        public string FirstName   { get; set; }
        public string MiddleName  { get; set; }
        public string LastName    { get; set; }
        public string Nickname    { get; set; }
        public string Company     { get; set; }
        public string Department  { get; set; }
        public string JobTitle    { get; set; }
        public string Notes       { get; set; }
        public string MobilePhone { get; set; }
        public string Birthday    { get; set; }
        public string FolderPath  { get; set; }
        public bool   IsDeleted   { get; set; }

        public List<string>    BusinessPhones { get; set; }
        public List<string>    HomePhones     { get; set; }
        public List<MsEmail>   Emails         { get; set; }
        public List<MsAddress> Addresses      { get; set; }

        public MsContact()
        {
            BusinessPhones = new List<string>();
            HomePhones     = new List<string>();
            Emails         = new List<MsEmail>();
            Addresses      = new List<MsAddress>();
        }
    }

    public class MsEmail
    {
        public string Name    { get; set; }
        public string Address { get; set; }
    }

    public class MsAddress
    {
        public string Type            { get; set; }
        public string Street          { get; set; }
        public string City            { get; set; }
        public string State           { get; set; }
        public string PostalCode      { get; set; }
        public string CountryOrRegion { get; set; }
    }
}
