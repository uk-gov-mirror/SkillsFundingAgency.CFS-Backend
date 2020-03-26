﻿using CalculateFunding.Common.Models;
using CalculateFunding.Common.Utility;
using CalculateFunding.Services.Publishing.Interfaces;
using CalculateFunding.Services.Publishing.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Publishing.Reporting
{
    public class GenerateApprovePublishedFundingCsvJobsCreation : BaseGeneratePublishedFundingCsvJobsCreation
    {
        public GenerateApprovePublishedFundingCsvJobsCreation(
            ICreateGeneratePublishedFundingCsvJobs createGeneratePublishedFundingCsvJobs,
            ICreateGeneratePublishedProviderEstateCsvJobs createGeneratePublishedProviderEstateCsvJob)
            : base(createGeneratePublishedFundingCsvJobs, createGeneratePublishedProviderEstateCsvJob)
        {
        }

        public override async Task CreateJobs(string specificationId, 
            string correlationId, 
            Reference user, 
            IEnumerable<string> fundingLineCodes = null, 
            IEnumerable<string> fundingStreamIds = null,
            string fundingPeriodId = null)
        {
            Guard.IsNullOrWhiteSpace(specificationId, nameof(specificationId));
            Guard.ArgumentNotNull(user, nameof(user));
            Guard.ArgumentNotNull(fundingLineCodes, nameof(fundingLineCodes));
            Guard.ArgumentNotNull(fundingStreamIds, nameof(fundingStreamIds));

            await CreatePublishedFundingCsvJobs(specificationId, correlationId, user, fundingLineCodes, fundingPeriodId);
        }

        public override bool IsForAction(GeneratePublishingCsvJobsCreationAction action)
        {
            return action == GeneratePublishingCsvJobsCreationAction.Approve;
        }
    }
}
