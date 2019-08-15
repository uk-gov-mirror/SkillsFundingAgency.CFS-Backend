using System.Collections.Generic;
using System.Linq;
using CalculateFunding.Common.TemplateMetadata.Models;
using CalculateFunding.Models.Calcs;
using Calculation = CalculateFunding.Common.TemplateMetadata.Models.Calculation;

namespace CalculateFunding.Services.Calcs
{
    public class TemplateContentsCalculationQuery : ITemplateContentsCalculationQuery
    {
        public Calculation GetTemplateContentsForMappingItem(TemplateMappingItem mappingItem,
            TemplateMetadataContents templateMetadataContents)
        {
            return GetCalculationFromFundingLines(mappingItem.TemplateId, templateMetadataContents.RootFundingLines);
        }

        private Calculation GetCalculationFromFundingLines(uint templateCalculationId,
            IEnumerable<FundingLine> fundingLines)
        {
            foreach (FundingLine fundingLine in fundingLines)
            {
                Calculation match = fundingLine?.Calculations.FirstOrDefault(_ => _.TemplateCalculationId == templateCalculationId);

                if (match == null && fundingLine.FundingLines?.Any() == true)
                    match = GetCalculationFromFundingLines(templateCalculationId, fundingLine.FundingLines);

                if (match != null)
                    return match;
            }

            return null;
        }    
    }
}