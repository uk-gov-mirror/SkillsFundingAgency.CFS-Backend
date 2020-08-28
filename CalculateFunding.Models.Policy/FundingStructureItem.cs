using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CalculateFunding.Models.Policy
{
    public class FundingStructureItem
    {
        public FundingStructureItem()
        {
        }

        public FundingStructureItem(
            int level,
            string name,
            string calculationId,
            string calculationPublishStatus, 
            FundingStructureType type, 
            string calculationType = null,
            List<FundingStructureItem> fundingStructureItems = null,
            string value = null, 
            DateTimeOffset? lastUpdatedDate = null)
        {
            Level = level;
            Name = name;
            CalculationId = calculationId;
            Type = type;
            CalculationPublishStatus = calculationPublishStatus;
            FundingStructureItems = fundingStructureItems;
            Value = value;
            CalculationType = calculationType;
            LastUpdatedDate = lastUpdatedDate;
        }
        
        [JsonProperty("level")]
        public int Level { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("calculationId")]
        public string CalculationId { get; set; }
        
        [JsonProperty("calculationPublishStatus")]
        public string CalculationPublishStatus { get; set; }
        
        [JsonProperty("type")]
        public FundingStructureType Type { get; set; }
        
        [JsonProperty("value")]
        public string Value { get; set; }
        
        [JsonProperty("calculationType")]
        public string CalculationType { get; }
        
        [JsonProperty("fundingStructureItems")]
        public ICollection<FundingStructureItem> FundingStructureItems { get; set; }
        
        [JsonProperty("lastUpdatedDate")]
        public DateTimeOffset? LastUpdatedDate { get; set; }
    }
}