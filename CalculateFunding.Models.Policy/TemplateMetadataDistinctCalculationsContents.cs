﻿using System.Collections.Generic;

namespace CalculateFunding.Models.Policy
{
    public class TemplateMetadataDistinctCalculationsContents
    {
        public string FundingStreamId { get; set; }
        public string FundingPeriodId { get; set; }
        public string TemplateVersion { get; set; }
        public IEnumerable<TemplateMetadataCalculation> Calculations { get; set; }
        public string SchemaVersion { get; set; }
    }
}
