﻿using Newtonsoft.Json;

namespace CalculateFunding.Models.FundingPolicy
{
    public class ProviderTypeMatch
    {
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [JsonProperty("providerSubtype")]
        public string ProviderSubtype { get; set; }
    }
}