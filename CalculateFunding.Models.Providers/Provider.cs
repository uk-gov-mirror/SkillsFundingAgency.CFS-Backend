﻿using CalculateFunding.Models.ProviderLegacy;
using Newtonsoft.Json;
using System;

namespace CalculateFunding.Models.Providers
{
    public class Provider
    {
        [JsonProperty("providerVersionId_providerId")]
        public string ProviderVersionIdProviderId;

        [JsonProperty("providerVersionId")]
        public string ProviderVersionId;

        [JsonProperty("providerId")]
        public string ProviderId;

        [JsonProperty("trustStatus")]
        public string TrustStatusViewModelString;

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("urn")]
        public string URN { get; set; }

        [JsonProperty("ukPrn")]
        public string UKPRN { get; set; }

        [JsonProperty("upin")]
        public string UPIN { get; set; }

        [JsonProperty("establishmentNumber")]
        public string EstablishmentNumber { get; set; }

        [JsonProperty("dfeEstablishmentNumber")]
        public string DfeEstablishmentNumber { get; set; }

        [JsonProperty("authority")]
        public string Authority { get; set; }

        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [JsonProperty("providerSubType")]
        public string ProviderSubType { get; set; }

        [JsonProperty("dateOpened")]
        public DateTimeOffset? DateOpened { get; set; }

        [JsonProperty("dateClosed")]
        public DateTimeOffset? DateClosed { get; set; }

        [JsonProperty("providerProfileIdType")]
        public string ProviderProfileIdType { get; set; }

        [JsonProperty("laCode")]
        public string LACode { get; set; }

        [JsonProperty("navVendorNo")]
        public string NavVendorNo { get; set; }

        [JsonProperty("crmAccountId")]
        public string CrmAccountId { get; set; }

        [JsonProperty("legalName")]
        public string LegalName { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("phaseOfEducation")]
        public string PhaseOfEducation { get; set; }

        [JsonProperty("reasonEstablishmentOpened")]
        public string ReasonEstablishmentOpened { get; set; }

        [JsonProperty("reasonEstablishmentClosed")]
        public string ReasonEstablishmentClosed { get; set; }

        [JsonProperty("successor")]
        public string Successor { get; set; }

        [JsonProperty("town")]
        public string Town { get; set; }

        [JsonProperty("postcode")]
        public string Postcode { get; set; }

        [JsonProperty("trustName")]
        public string TrustName { get; set; }

        [JsonProperty("trustCode")]
        public string TrustCode { get; set; }

        [JsonProperty("companiesHouseNumber")]
        public string CompaniesHouseNumber { get; set; }

        [JsonProperty("groupIdNumber")]
        public string GroupIdNumber { get; set; }

        [JsonProperty("rscRegionName")]
        public string RscRegionName { get; set; }

        [JsonProperty("rscRegionCode")]
        public string RscRegionCode { get; set; }

        [JsonProperty("governmentOfficeRegionName")]
        public string GovernmentOfficeRegionName { get; set; }

        [JsonProperty("governmentOfficeRegionCode")]
        public string GovernmentOfficeRegionCode { get; set; }

        [JsonProperty("districtName")]
        public string DistrictName { get; set; }

        [JsonProperty("districtCode")]
        public string DistrictCode { get; set; }

        [JsonProperty("wardName")]
        public string WardName { get; set; }

        [JsonProperty("wardCode")]
        public string WardCode { get; set; }

        [JsonProperty("censusWardName")]
        public string CensusWardName { get; set; }

        [JsonProperty("censusWardCode")]
        public string CensusWardCode { get; set; }

        [JsonProperty("middleSuperOutputAreaName")]
        public string MiddleSuperOutputAreaName { get; set; }

        [JsonProperty("middleSuperOutputAreaCode")]
        public string MiddleSuperOutputAreaCode { get; set; }

        [JsonProperty("lowerSuperOutputAreaName")]
        public string LowerSuperOutputAreaName { get; set; }

        [JsonProperty("lowerSuperOutputAreaCode")]
        public string LowerSuperOutputAreaCode { get; set; }

        [JsonProperty("parliamentaryConstituencyName")]
        public string ParliamentaryConstituencyName { get; set; }

        [JsonProperty("parliamentaryConstituencyCode")]
        public string ParliamentaryConstituencyCode { get; set; }

        [JsonProperty("countryCode")]
        public string CountryCode { get; set; }

        [JsonProperty("countryName")]
        public string CountryName { get; set; }

        [JsonProperty("localGovernmentGroupTypeCode")]
        public string LocalGovernmentGroupTypeCode { get; set; }

        [JsonProperty("localGovernmentGroupTypeName")]
        public string LocalGovernmentGroupTypeName { get; set; }

        [JsonProperty("street")]
        public string Street { get; set; }

        [JsonProperty("locality")]
        public string Locality { get; set; }

        [JsonProperty("address3")]
        public string Address3 { get; set; }

        [JsonIgnore]
        public TrustStatus TrustStatus
        {
            get
            {
                if (Enum.TryParse<TrustStatus>(TrustStatusViewModelString, true, out var result))
                {
                    return result;
                }

                return TrustStatus.NotApplicable;
            }
            set
            {
                TrustStatusViewModelString = value.ToString();
            }
        }
    }
}
