﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.Models.HealthCheck;
using CalculateFunding.Common.TemplateMetadata;
using CalculateFunding.Common.TemplateMetadata.Schema11;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Policy;
using CalculateFunding.Models.Policy.TemplateBuilder;
using CalculateFunding.Models.Versioning;
using CalculateFunding.Repositories.Common.Search;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Policy.Interfaces;
using CalculateFunding.Services.Policy.Models;
using CalculateFunding.Services.Policy.Validators;
using FluentValidation.Results;
using Serilog;
using Serilog.Core;

namespace CalculateFunding.Services.Policy.TemplateBuilder
{
    public class TemplateBuilderService : ITemplateBuilderService, IHealthChecker
    {
        private readonly IIoCValidatorFactory _validatorFactory;
        private readonly IFundingTemplateValidationService _fundingTemplateValidationService;
        private readonly ITemplateMetadataResolver _templateMetadataResolver;
        private readonly ITemplateVersionRepository _templateVersionRepository;
        private readonly ILogger _logger;
        private readonly ITemplateRepository _templateRepository;
        private readonly ISearchRepository<TemplateIndex> _searchRepository;
        private readonly IPolicyRepository _policyRepository;

        public TemplateBuilderService(
            IIoCValidatorFactory validatorFactory,
            IFundingTemplateValidationService fundingTemplateValidationService,
            ITemplateMetadataResolver templateMetadataResolver,
            ITemplateVersionRepository templateVersionRepository,
            ITemplateRepository templateRepository,
            ISearchRepository<TemplateIndex> searchRepository,
            IPolicyRepository policyRepository,
            ILogger logger)
        {
            _validatorFactory = validatorFactory;
            _fundingTemplateValidationService = fundingTemplateValidationService;
            _templateMetadataResolver = templateMetadataResolver;
            _templateVersionRepository = templateVersionRepository;
            _templateRepository = templateRepository;
            _searchRepository = searchRepository;
            _policyRepository = policyRepository;
            _logger = logger;
        }

        public async Task<ServiceHealth> IsHealthOk()
        {
            ServiceHealth templateRepoHealth = await ((IHealthChecker) _templateRepository).IsHealthOk();
            ServiceHealth templateVersionRepoHealth = await _templateVersionRepository.IsHealthOk();
            (bool Ok, string Message) = await _searchRepository.IsHealthOk();

            ServiceHealth health = new ServiceHealth
            {
                Name = GetType().Name
            };
            health.Dependencies.AddRange(templateRepoHealth.Dependencies);
            health.Dependencies.AddRange(templateVersionRepoHealth.Dependencies);
            health.Dependencies.Add(new DependencyHealth { HealthOk = Ok, DependencyName = _searchRepository.GetType().GetFriendlyName(), Message = Message });

            return health;
        }

        public async Task<TemplateResponse> GetTemplate(string templateId)
        {
            Guard.IsNotEmpty(templateId, nameof(templateId));

            var template = await _templateRepository.GetTemplate(templateId);
            if (template == null)
            {
                return null;
            }

            return Map(template.Current);
        }

        public async Task<TemplateResponse> GetTemplateVersion(string templateId, string version)
        {
            Guard.IsNotEmpty(templateId, nameof(templateId));
            if (!int.TryParse(version, out int versionNumber))
                return null;

            var templateVersion = await _templateVersionRepository.GetTemplateVersion(templateId, versionNumber);
            if (templateVersion == null)
            {
                return null;
            }

            return Map(templateVersion);
        }

        public async Task<IEnumerable<TemplateResponse>> GetVersionsByTemplate(string templateId, List<TemplateStatus> statuses)
        {
            Guard.ArgumentNotNull(templateId, nameof(templateId));

            IEnumerable<TemplateVersion> templateVersions = await _templateVersionRepository.GetByTemplate(templateId, statuses);

            return templateVersions.Select(Map).ToList();
        }

        public async Task<IEnumerable<TemplateResponse>> FindVersionsByFundingStreamAndPeriod(FindTemplateVersionQuery query)
        {
            IEnumerable<TemplateVersion> templateVersions = await _templateVersionRepository
                .FindByFundingStreamAndPeriod(query);

            return templateVersions.Select(Map).ToList();
        }

        public async Task<CommandResult> CreateTemplate(
            TemplateCreateCommand command,
            Reference author)
        {
            ValidationResult validatorResult = await _validatorFactory.Validate(command);
            validatorResult.Errors.AddRange((await _validatorFactory.Validate(author))?.Errors);
            
            if (!validatorResult.IsValid)
            {
                return CommandResult.ValidationFail(validatorResult);
            }

            try
            {
                if (await _templateRepository.IsTemplateNameInUse(command.Name))
                {
                    return CommandResult.ValidationFail(nameof(command.Name), $"Template name [{command.Name}] already in use");
                }

                if (await _templateRepository.IsFundingStreamAndPeriodInUse(command.FundingStreamId, command.FundingPeriodId))
                {
                    string validationErrorMessage =
                        $"Template with FundingStreamId [{command.FundingStreamId}] and FundingPeriodId [{command.FundingPeriodId}] already in use";
                    _logger.Error(validationErrorMessage);
                    ValidationResult validationResult = new ValidationResult();
                    validationResult.WithError(nameof(command.FundingPeriodId), validationErrorMessage);
                    validationResult.WithError(nameof(command.FundingStreamId), validationErrorMessage);
                    return new CommandResult
                    {
                        ErrorMessage = validationErrorMessage,
                        ValidationResult = validationResult
                    };
                }

                Template template = new Template
                {
                    TemplateId = Guid.NewGuid().ToString(),
                    Name = command.Name
                };
                template.Current = new TemplateVersion
                {
                    TemplateId = template.TemplateId,
                    FundingStreamId = command.FundingStreamId,
                    FundingPeriodId = command.FundingPeriodId,
                    Name = command.Name,
                    Description = command.Description,
                    Version = 1,
                    MajorVersion = 0,
                    MinorVersion = 1,
                    PublishStatus = PublishStatus.Draft,
                    SchemaVersion = command.SchemaVersion,
                    Author = author,
                    Date = DateTimeOffset.Now.ToLocalTime()
                };

                HttpStatusCode result = await _templateRepository.CreateDraft(template);

                if (result.IsSuccess())
                {
                    await _templateVersionRepository.SaveVersion(template.Current);
                    await CreateTemplateIndexItem(template, author);

                    return new CommandResult
                    {
                        Succeeded = true,
                        TemplateId = template.TemplateId
                    };
                }

                string errorMessage = $"Failed to create a new template with name {command.Name} in Cosmos. Status code {(int) result}";
                _logger.Error(errorMessage);

                return new CommandResult
                {
                    ErrorMessage = errorMessage
                };
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    Exception = ex
                };
            }
        }

        public async Task<CommandResult> CreateTemplateAsClone(TemplateCreateAsCloneCommand command, Reference author)
        {
            ValidationResult validatorResult = await _validatorFactory.Validate(command);
            validatorResult.Errors.AddRange((await _validatorFactory.Validate(author))?.Errors);
            
            if (!validatorResult.IsValid)
            {
                return CommandResult.ValidationFail(validatorResult);
            }

            try
            {
                var sourceTemplate = await _templateRepository.GetTemplate(command.CloneFromTemplateId);
                if (sourceTemplate == null)
                {
                    return CommandResult.ValidationFail(nameof(command.CloneFromTemplateId), "Template doesn't exist");
                }

                var sourceVersion = sourceTemplate.Current;
                if (command.Version != null)
                {
                    if (!int.TryParse(command.Version, out int versionNumber))
                    {
                        return CommandResult.ValidationFail(nameof(command.Version), $"Invalid version '{command.Version}'");
                    }

                    sourceVersion = await _templateVersionRepository.GetTemplateVersion(command.CloneFromTemplateId, versionNumber);
                    if (sourceVersion == null)
                    {
                        return CommandResult.ValidationFail(nameof(command.Version),
                            $"Version '{command.Version}' could not be found for template '{command.CloneFromTemplateId}'");
                    }
                }

                if (await _templateRepository.IsTemplateNameInUse(command.Name))
                {
                    return CommandResult.ValidationFail(nameof(command.Name), $"Template name [{command.Name}] already in use");
                }

                if (await _templateRepository.IsFundingStreamAndPeriodInUse(command.FundingStreamId, command.FundingPeriodId))
                {
                    string validationErrorMessage =
                        $"Template with FundingStreamId [{command.FundingStreamId}] and FundingPeriodId [{command.FundingPeriodId}] already in use";
                    _logger.Error(validationErrorMessage);
                    ValidationResult validationResult = new ValidationResult();
                    validationResult.Errors.Add(new ValidationFailure(nameof(command.FundingStreamId), validationErrorMessage));
                    validationResult.Errors.Add(new ValidationFailure(nameof(command.FundingPeriodId), validationErrorMessage));
                    return new CommandResult
                    {
                        ErrorMessage = validationErrorMessage,
                        ValidationResult = validationResult
                    };
                }

                Guid templateId = Guid.NewGuid();
                Template template = new Template
                {
                    TemplateId = templateId.ToString(), 
                    Name = command.Name,
                    Current = new TemplateVersion
                    {
                        TemplateId = templateId.ToString(),
                        SchemaVersion = sourceVersion.SchemaVersion,
                        Author = author,
                        Name = command.Name,
                        Description = command.Description,
                        FundingStreamId = command.FundingStreamId,
                        FundingPeriodId = command.FundingPeriodId,
                        Comment = null,
                        TemplateJson = sourceVersion.TemplateJson,
                        Status = TemplateStatus.Draft,
                        Version = 1,
                        MajorVersion = 0,
                        MinorVersion = 1,
                        Date = DateTimeOffset.Now
                    }
                };
                
                // create new version and save it
                HttpStatusCode templateVersionUpdateResult = await _templateVersionRepository.SaveVersion(template.Current);
                if (!templateVersionUpdateResult.IsSuccess())
                {
                    return CommandResult.ValidationFail(nameof(command.Version), $"Template version failed to save: {templateVersionUpdateResult}");
                }

                HttpStatusCode result = await _templateRepository.CreateDraft(template);

                if (result.IsSuccess())
                {
                    await CreateTemplateIndexItem(template, author);
                    
                    return new CommandResult
                    {
                        Succeeded = true,
                        TemplateId = template.TemplateId
                    };
                }

                string errorMessage = $"Failed to create a new template with name {command.Name} in Cosmos. Status code {(int) result}";
                _logger.Error(errorMessage);
                return CommandResult.Fail(errorMessage);
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    Exception = ex
                };
            }
        }

        private async Task CreateTemplateIndexItem(Template template, Reference author)
        {
            var fundingPeriod = await _policyRepository.GetFundingPeriodById(template.Current.FundingPeriodId);
            var fundingStream = await _policyRepository.GetFundingStreamById(template.Current.FundingStreamId);

            TemplateIndex templateIndex = new TemplateIndex
            {
                Id = template.TemplateId,
                Name = template.Current.Name,
                FundingStreamId = template.Current.FundingStreamId,
                FundingStreamName = fundingStream.ShortName,
                FundingPeriodId = template.Current.FundingPeriodId,
                FundingPeriodName = fundingPeriod.Name,
                LastUpdatedAuthorId = author.Id,
                LastUpdatedAuthorName = author.Name,
                LastUpdatedDate = DateTimeOffset.Now,
                Version = template.Current.Version,
                CurrentMajorVersion = template.Current.MajorVersion,
                CurrentMinorVersion = template.Current.MinorVersion,
                PublishedMajorVersion = template.Released?.MajorVersion ?? 0,
                PublishedMinorVersion = template.Released?.MinorVersion ?? 0,
                HasReleasedVersion = template.Released != null ? "Yes" : "No"
            };

            await _searchRepository.Index(new List<TemplateIndex>
            {
                templateIndex
            });
        }

        public async Task<CommandResult> UpdateTemplateContent(TemplateContentUpdateCommand command, Reference author)
        {
            // input parameter validation
            ValidationResult validatorResult = await _validatorFactory.Validate(command);
            validatorResult.Errors.AddRange((await _validatorFactory.Validate(author))?.Errors);
            
            if (!validatorResult.IsValid)
            {
                return CommandResult.ValidationFail(validatorResult);
            }

            var template = await _templateRepository.GetTemplate(command.TemplateId);
            if (template == null)
            {
                return CommandResult.ValidationFail(nameof(command.TemplateId), "Template doesn't exist");
            }

            if (template.Current.TemplateJson == command.TemplateJson)
            {
                return CommandResult.Success();
            }

            CommandResult validationError = await ValidateTemplateContent(command);
            if (validationError != null)
                return validationError;

            var updated = await UpdateTemplateContent(command, author, template);

            if (!updated.IsSuccess())
            {
                return CommandResult.Fail($"Failed to update template: {updated}");
            }

            return CommandResult.Success();
        }

        public async Task<CommandResult> UpdateTemplateMetadata(TemplateMetadataUpdateCommand command, Reference author)
        {
            ValidationResult validatorResult = await _validatorFactory.Validate(command);
            validatorResult.Errors.AddRange((await _validatorFactory.Validate(author))?.Errors);
            
            if (!validatorResult.IsValid)
            {
                return CommandResult.ValidationFail(validatorResult);
            }

            var template = await _templateRepository.GetTemplate(command.TemplateId);
            if (template == null)
            {
                return CommandResult.ValidationFail(nameof(command.TemplateId), "Template doesn't exist");
            }

            if (template.Current.Name == command.Name && template.Current.Description == command.Description)
            {
                return CommandResult.Success();
            }

            // validate template name is unique if it is changing
            if (template.Current.Name != command.Name)
            {
                if (await _templateRepository.IsTemplateNameInUse(command.Name))
                {
                    return CommandResult.ValidationFail(nameof(command.Name), $"Template name [{command.Name}] already in use");
                }
            }

            var updated = await UpdateTemplateMetadata(command, author, template);

            if (!updated.IsSuccess())
            {
                return CommandResult.Fail($"Failed to update template: {updated}");
            }

            return CommandResult.Success();
        }

        public async Task<CommandResult> ApproveTemplate(Reference author, string templateId, string comment, string version = null)
        {
            Guard.IsNotEmpty(templateId, nameof(templateId));
            Guard.ArgumentNotNull(author, nameof(author));

            var template = await _templateRepository.GetTemplate(templateId);
            if (template == null)
            {
                return CommandResult.ValidationFail(nameof(templateId), "Template doesn't exist");
            }

            var templateVersion = template.Current;
            if (version != null)
            {
                if (!int.TryParse(version, out int versionNumber))
                {
                    return CommandResult.ValidationFail(nameof(version), $"Invalid version '{version}'");
                }

                templateVersion = await _templateVersionRepository.GetTemplateVersion(templateId, versionNumber);
                if (templateVersion == null)
                {
                    return CommandResult.ValidationFail(nameof(version), $"Version '{version}' could not be found for template '{templateId}'");
                }
            }

            if (templateVersion.Status == TemplateStatus.Published)
            {
                return CommandResult.ValidationFail(nameof(templateVersion.Status), "Template version is already published");
            }

            // create new version and save it
            TemplateVersion newVersion = templateVersion.Clone() as TemplateVersion;
            newVersion.Author = author;
            newVersion.Name = templateVersion.Name;
            newVersion.Description = templateVersion.Description;
            newVersion.Comment = comment;
            newVersion.TemplateJson = templateVersion.TemplateJson;
            newVersion.Status = TemplateStatus.Published;
            newVersion.Version = template.Current.Version + 1;
            newVersion.MajorVersion = template.Current.MajorVersion + 1;
            newVersion.MinorVersion = 0;
            newVersion.Date = DateTimeOffset.Now;
            newVersion.Predecessors ??= new List<string>();
            newVersion.Predecessors.Add(template.Current.Id);
            var templateVersionUpdateResult = await _templateVersionRepository.SaveVersion(newVersion);
            if (!templateVersionUpdateResult.IsSuccess())
            {
                return CommandResult.ValidationFail(nameof(templateId), $"Template version failed to save: {templateVersionUpdateResult}");
            }

            // update template
            template.Name = newVersion.Name;
            template.AddPredecessor(template.Current.Id);
            template.Current = newVersion;
            var templateUpdateResult = await _templateRepository.Update(template);
            if (!templateUpdateResult.IsSuccess())
            {
                return CommandResult.Fail($"Failed to approve template: {templateUpdateResult}");
            }

            return CommandResult.Success();
        }

        private static TemplateResponse Map(TemplateVersion template)
        {
            return new TemplateResponse
            {
                TemplateId = template.TemplateId,
                TemplateJson = template.TemplateJson,
                Name = template.Name,
                Description = template.Description,
                FundingStreamId = template.FundingStreamId,
                FundingPeriodId = template.FundingPeriodId,
                Version = template.Version,
                MinorVersion = template.MinorVersion,
                MajorVersion = template.MajorVersion,
                SchemaVersion = template.SchemaVersion,
                Status = template.Status,
                AuthorId = template.Author.Id,
                AuthorName = template.Author.Name,
                LastModificationDate = template.Date.DateTime,
                PublishStatus = template.PublishStatus,
                Comments = template.Comment
            };
        }

        private async Task<HttpStatusCode> UpdateTemplateContent(TemplateContentUpdateCommand command, Reference author, Template template)
        {
            // create new version and save it
            TemplateVersion newVersion = template.Current.Clone() as TemplateVersion;
            if (template.Current.Status == TemplateStatus.Published)
            {
                newVersion.Status = TemplateStatus.Draft;
            }

            newVersion.Author = author;
            newVersion.Name = template.Current.Name;
            newVersion.Comment = null;
            newVersion.Description = template.Current.Description;
            newVersion.Status = TemplateStatus.Draft;
            newVersion.Date = DateTimeOffset.Now;
            newVersion.TemplateJson = command.TemplateJson;
            newVersion.Version++;
            newVersion.MinorVersion++;
            newVersion.Predecessors ??= new List<string>();
            newVersion.Predecessors.Add(template.Current.Id);
            await _templateVersionRepository.SaveVersion(newVersion);

            // update template
            template.Name = template.Current.Name;
            template.AddPredecessor(template.Current.Id);
            template.Current = newVersion;
            
            HttpStatusCode updateResult = await _templateRepository.Update(template);
            if (updateResult == HttpStatusCode.OK)
            {
                await CreateTemplateIndexItem(template, author);
            }

            return updateResult;
        }

        private async Task<HttpStatusCode> UpdateTemplateMetadata(TemplateMetadataUpdateCommand command, Reference author, Template template)
        {
            // create new version and save it
            TemplateVersion newVersion = template.Current.Clone() as TemplateVersion;
            newVersion.Author = author;
            newVersion.Comment = null;
            newVersion.Status = TemplateStatus.Draft;
            newVersion.Date = DateTimeOffset.Now;
            newVersion.TemplateJson = template.Current.TemplateJson;
            newVersion.Name = command.Name;
            newVersion.Description = command.Description;
            newVersion.Version++;
            newVersion.MinorVersion++;
            newVersion.Predecessors ??= new List<string>();
            newVersion.Predecessors.Add(template.Current.Id);
            var result = await _templateVersionRepository.SaveVersion(newVersion);
            if (!result.IsSuccess())
                return result;

            // update template
            template.Name = template.Current.Name;
            template.AddPredecessor(template.Current.Id);
            template.Current = newVersion;

            HttpStatusCode updateResult = await _templateRepository.Update(template);
            if (updateResult == HttpStatusCode.OK)
            {
                await CreateTemplateIndexItem(template, author);
            }

            return updateResult;
        }

        private async Task<CommandResult> ValidateTemplateContent(TemplateContentUpdateCommand command)
        {
            // template json validation
            FundingTemplateValidationResult validationResult = await _fundingTemplateValidationService.ValidateFundingTemplate(command.TemplateJson);

            if (!validationResult.IsValid)
            {
                return CommandResult.ValidationFail(validationResult);
            }

            // schema specific validation
            ITemplateMetadataGenerator templateMetadataGenerator = _templateMetadataResolver.GetService(validationResult.SchemaVersion);

            ValidationResult validationGeneratorResult = templateMetadataGenerator.Validate(command.TemplateJson);

            if (!validationGeneratorResult.IsValid)
            {
                return CommandResult.ValidationFail(validationGeneratorResult);
            }

            return null;
        }
    }
}