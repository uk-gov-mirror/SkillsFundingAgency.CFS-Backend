﻿using System.Collections.Generic;
using CalculateFunding.Common.ApiClient.Calcs.Models;
using CalculateFunding.Common.TemplateMetadata.Models;
using CalculateFunding.Models.Publishing;

namespace CalculateFunding.Services.Publishing.Interfaces
{
    public interface IFundingLineTotalAggregator
    {
        IEnumerable<Models.Publishing.FundingLine> GenerateTotals(TemplateMetadataContents templateMetadataContents, TemplateMapping mapping, IEnumerable<CalculationResult> calculationResults);
    }
}
