﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace Allocations.Models.Specs
{
    public class DatasetDefinition
    {
        [JsonProperty("id")]
        public string Id => Name.ToSlug();

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("fieldDefinitions")]
        public List<DatasetFieldDefinition> FieldDefinitions { get; set; }
    }
}