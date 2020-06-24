﻿using System.Collections.Generic;
using System.Threading.Tasks;
using CalculateFunding.Common.Models;
using CalculateFunding.Models.Policy.TemplateBuilder;
using CalculateFunding.Services.Policy.Models;

namespace CalculateFunding.Services.Policy.Interfaces
{
    public interface ITemplateBuilderService
    {
        Task<TemplateResponse> GetTemplate(string templateId);

        Task<TemplateResponse> GetTemplateVersion(string templateId, string version);

        Task<IEnumerable<TemplateSummaryResponse>> GetVersionSummariesByTemplate(string templateId, List<TemplateStatus> statuses);

        Task<IEnumerable<TemplateSummaryResponse>> FindVersionsByFundingStreamAndPeriod(FindTemplateVersionQuery query);
        
        Task<CommandResult> CreateTemplate(TemplateCreateCommand command, Reference author);

        Task<CommandResult> UpdateTemplateContent(TemplateFundingLinesUpdateCommand originalCommand, Reference author);

        Task<CommandResult> UpdateTemplateMetadata(TemplateMetadataUpdateCommand command, Reference author);

        Task<CommandResult> PublishTemplate(TemplatePublishCommand command);

        Task<CommandResult> CreateTemplateAsClone(TemplateCreateAsCloneCommand command, Reference author);
        
        Task<IEnumerable<FundingStreamWithPeriods>> GetFundingStreamAndPeriodsWithoutTemplates();
    }
}