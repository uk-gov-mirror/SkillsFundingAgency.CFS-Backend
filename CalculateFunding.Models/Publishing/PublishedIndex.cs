﻿using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.Search;
using Newtonsoft.Json;

namespace CalculateFunding.Models.Publishing
{
    [SearchIndex(IndexerType = IndexerType.Search, IndexName = "publishedindex")]
    public class PublishedIndex
    {
        [Key]
        [IsSearchable, IsRetrievable(true)]
        [JsonProperty("id")]
        public string Id { get; set; }

        [IsFilterable, IsFacetable, IsRetrievable(true)]
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [IsFilterable, IsFacetable, IsRetrievable(true)]
        [JsonProperty("localAuthority")]
        public string LocalAuthority { get; set; }

        [IsFilterable, IsFacetable, IsRetrievable(true)]
        [JsonProperty("fundingStatus")]
        public string FundingStatus { get; set; }

        [IsFilterable, IsSortable, IsSearchable, IsRetrievable(true)]
        [JsonProperty("providerName")]
        public string ProviderName { get; set; }

        [IsFilterable, IsSearchable, IsRetrievable(true)]
        [JsonProperty("ukprn")]
        public string UKPRN { get; set; }

        [IsFilterable, IsSearchable, IsRetrievable(true)]
        [JsonProperty("fundingValue")]
        public double FundingValue { get; set; }

        [IsFilterable, IsRetrievable(true)]
        [JsonProperty("specificationId")]
        public string SpecificationId { get; set; }

        [IsFilterable, IsRetrievable(true)]
        [JsonProperty("fundingStreamIds")]
        public string[] FundingStreamIds { get; set; }

        [IsFilterable, IsRetrievable(true)]
        [JsonProperty("fundingPeriodId")]
        public string FundingPeriodId { get; set; }
    }
}
