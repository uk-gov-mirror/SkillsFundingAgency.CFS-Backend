﻿using System;
using System.Collections.Generic;
using System.Text;
using CalculateFunding.Models.Results;
using Newtonsoft.Json;

namespace CalculateFunding.Models.Scenarios
{
    public class TestSuite : Reference
    {
        [JsonProperty("specification")]
        public Reference Specification { get; set; }
        [JsonProperty("calculationTests")]
        public List<CalculationTest> CalculationTests { get; set; }
        [JsonProperty("testProviders")]
        public List<ProviderSummary> TestProviders { get; set; }
    }
}
