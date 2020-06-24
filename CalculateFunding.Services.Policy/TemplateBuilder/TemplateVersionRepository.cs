﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Common.CosmosDb;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Policy.TemplateBuilder;
using CalculateFunding.Services.Core.Services;
using CalculateFunding.Services.Policy.Models;

namespace CalculateFunding.Services.Policy.TemplateBuilder
{
    public class TemplateVersionRepository : VersionRepository<TemplateVersion>, ITemplateVersionRepository
    {
        public TemplateVersionRepository(ICosmosRepository cosmosRepository) : base(cosmosRepository)
        {
        }

        public async Task<TemplateVersion> GetTemplateVersion(string templateId, int versionNumber)
        {
            Guard.IsNullOrWhiteSpace(templateId, nameof(templateId));

            IEnumerable<TemplateVersion> templateMatches = await _cosmosRepository.Query<TemplateVersion>(x =>
                x.Content.TemplateId == templateId && x.Content.Version == versionNumber);

            if (templateMatches == null || !templateMatches.Any())
            {
                return null;
            }

            if (templateMatches.Count() == 1)
            {
                return templateMatches.First();
            }

            throw new ApplicationException($"Duplicate templates with TemplateId={templateId}");
        }

        public async Task<IEnumerable<TemplateSummaryResponse>> GetSummaryVersionsByTemplate(string templateId, IEnumerable<TemplateStatus> statuses)
        {
            Guard.IsNullOrWhiteSpace(templateId, nameof(templateId));

            List<TemplateStatus> templateStatuses = statuses.ToList();
            if (templateStatuses.Any())
            {
                return await _cosmosRepository.Query<TemplateSummaryResponse>(x =>
                    x.Content.TemplateId == templateId && templateStatuses.Contains(x.Content.Status));
            }

            return await _cosmosRepository.Query<TemplateSummaryResponse>(x =>
                x.Content.TemplateId == templateId);
        }

        public async Task<IEnumerable<TemplateSummaryResponse>> FindByFundingStreamAndPeriod(FindTemplateVersionQuery query)
        {
            Guard.IsNullOrWhiteSpace(query.FundingStreamId, nameof(query.FundingStreamId));
            Guard.IsNullOrWhiteSpace(query.FundingPeriodId, nameof(query.FundingPeriodId));
            
            query.Statuses ??= new List<TemplateStatus>();
            if (query.Statuses.Any())
            {
                return await _cosmosRepository.Query<TemplateSummaryResponse>(x =>
                    x.Content.FundingStreamId == query.FundingStreamId 
                    && x.Content.FundingPeriodId == query.FundingPeriodId 
                    && query.Statuses.Contains(x.Content.Status));
            }

            return await _cosmosRepository.Query<TemplateSummaryResponse>(x =>
                x.Content.FundingStreamId == query.FundingStreamId 
                && x.Content.FundingPeriodId == query.FundingPeriodId);
        }
    }
}