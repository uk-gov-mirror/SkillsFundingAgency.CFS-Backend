﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Common.ApiClient.Calcs;
using CalculateFunding.Common.ApiClient.Calcs.Models;
using CalculateFunding.Common.ApiClient.Jobs;
using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.ApiClient.Policies;
using CalculateFunding.Common.ApiClient.Policies.Models;
using CalculateFunding.Common.ApiClient.Specifications.Models;
using CalculateFunding.Common.JobManagement;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.TemplateMetadata.Models;
using CalculateFunding.Common.Utility;
using CalculateFunding.Generators.OrganisationGroup.Interfaces;
using CalculateFunding.Generators.OrganisationGroup.Models;
using CalculateFunding.Models.Publishing;
using CalculateFunding.Repositories.Common.Search;
using CalculateFunding.Services.Core;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Publishing.Interfaces;
using Microsoft.Azure.ServiceBus;
using Polly;
using Serilog;
using FundingConfiguration = CalculateFunding.Common.ApiClient.Policies.Models.FundingConfig.FundingConfiguration;
using Provider = CalculateFunding.Common.ApiClient.Providers.Models.Provider;

namespace CalculateFunding.Services.Publishing
{
    public class PublishService : IPublishService
    {
        private readonly IPublishedFundingStatusUpdateService _publishedFundingStatusUpdateService;
        private readonly IPublishedFundingDataService _publishedFundingDataService;
        private readonly ISpecificationService _specificationService;

        private readonly IProviderService _providerService;

        private readonly IJobsApiClient _jobsApiClient;
        private readonly ILogger _logger;
        private readonly ICalculationsApiClient _calculationsApiClient;
        private readonly IPoliciesApiClient _policiesApiClient;
        private readonly IPublishPrerequisiteChecker _publishPrerequisiteChecker;
        private readonly IPublishedFundingChangeDetectorService _publishedFundingChangeDetectorService;
        private readonly IPublishedFundingGenerator _publishedFundingGenerator;
        private readonly IPublishedProviderDataGenerator _publishedProviderDataGenerator;
        private readonly ICalculationResultsService _calculationResultsService;
        private readonly IPublishedFundingContentsPersistanceService _publishedFundingContentsPersistanceService;
        private readonly IPublishedProviderContentPersistanceService _publishedProviderContentsPersistanceService;
        private readonly IPublishedProviderContentsGeneratorResolver _publishedProviderContentsGeneratorResolver;
        private readonly IPublishedFundingDateService _publishedFundingDateService;
        private readonly IPublishedProviderStatusUpdateService _publishedProviderStatusUpdateService;
        private readonly IOrganisationGroupGenerator _organisationGroupGenerator;
        private readonly ISearchRepository<PublishedFundingIndex> _publishedFundingSearchRepository;
        private readonly IPublishedProviderIndexerService _publishedProviderIndexerService;
        private readonly Policy _publishingResiliencePolicy;
        private readonly Policy _jobsApiClientPolicy;
        private readonly Policy _calculationsApiClientPolicy;
        private readonly Policy _policyApiClientPolicy;
        private readonly IPublishingEngineOptions _publishingEngineOptions;
        private readonly IJobManagement _jobManagement;
        private readonly IProfilingService _profilingService;

        public PublishService(IPublishedFundingStatusUpdateService publishedFundingStatusUpdateService,
            IPublishedFundingDataService publishedFundingDataService,
            IPublishingResiliencePolicies publishingResiliencePolicies,
            ISpecificationService specificationService,
            IOrganisationGroupGenerator organisationGroupGenerator,
            IPublishPrerequisiteChecker publishPrerequisiteChecker,
            IPublishedFundingChangeDetectorService publishedFundingChangeDetectorService,
            IPublishedFundingGenerator publishedFundingGenerator,
            IPublishedProviderDataGenerator publishedProviderDataGenerator,
            IPublishedProviderContentsGeneratorResolver publishedProviderContentsGeneratorResolver,
            IPublishedFundingContentsPersistanceService publishedFundingContentsPersistanceService,
            IPublishedProviderContentPersistanceService publishedProviderContentsPersistanceService,
            IPublishedFundingDateService publishedFundingDateService,
            IPublishedProviderStatusUpdateService publishedProviderStatusUpdateService,
            IProviderService providerService,
            ICalculationResultsService calculationResultsService,
            ISearchRepository<PublishedFundingIndex> publishedFundingSearchRepository,
            IPublishedProviderIndexerService publishedProviderIndexerService,
            IJobsApiClient jobsApiClient,
            IPoliciesApiClient policiesApiClient,
            ICalculationsApiClient calculationsApiClient,
            ILogger logger,
            IPublishingEngineOptions publishingEngineOptions,
            IJobManagement jobManagement,
            IProfilingService profilingService)
        {
            Guard.ArgumentNotNull(publishedFundingStatusUpdateService, nameof(publishedFundingStatusUpdateService));
            Guard.ArgumentNotNull(publishedFundingDataService, nameof(publishedFundingDataService));
            Guard.ArgumentNotNull(publishingResiliencePolicies, nameof(publishingResiliencePolicies));
            Guard.ArgumentNotNull(specificationService, nameof(specificationService));
            Guard.ArgumentNotNull(organisationGroupGenerator, nameof(organisationGroupGenerator));
            Guard.ArgumentNotNull(publishPrerequisiteChecker, nameof(publishPrerequisiteChecker));
            Guard.ArgumentNotNull(publishedFundingChangeDetectorService, nameof(publishedFundingChangeDetectorService));
            Guard.ArgumentNotNull(publishedFundingGenerator, nameof(publishedFundingGenerator));
            Guard.ArgumentNotNull(publishedProviderDataGenerator, nameof(publishedProviderDataGenerator));
            Guard.ArgumentNotNull(calculationResultsService, nameof(calculationResultsService));
            Guard.ArgumentNotNull(publishedFundingGenerator, nameof(publishedProviderContentsGeneratorResolver));
            Guard.ArgumentNotNull(publishedFundingContentsPersistanceService, nameof(publishedFundingContentsPersistanceService));
            Guard.ArgumentNotNull(publishedProviderContentsPersistanceService, nameof(publishedProviderContentsPersistanceService));
            Guard.ArgumentNotNull(publishedFundingDateService, nameof(publishedFundingDateService));
            Guard.ArgumentNotNull(publishedProviderStatusUpdateService, nameof(publishedProviderStatusUpdateService));
            Guard.ArgumentNotNull(publishedFundingSearchRepository, nameof(publishedFundingSearchRepository));
            Guard.ArgumentNotNull(publishedProviderIndexerService, nameof(publishedProviderIndexerService));
            Guard.ArgumentNotNull(providerService, nameof(providerService));
            Guard.ArgumentNotNull(jobsApiClient, nameof(jobsApiClient));
            Guard.ArgumentNotNull(policiesApiClient, nameof(policiesApiClient));
            Guard.ArgumentNotNull(calculationsApiClient, nameof(calculationsApiClient));
            Guard.ArgumentNotNull(logger, nameof(logger));
            Guard.ArgumentNotNull(publishingEngineOptions, nameof(publishingEngineOptions));
            Guard.ArgumentNotNull(profilingService, nameof(profilingService));

            Guard.ArgumentNotNull(publishingResiliencePolicies.PublishedFundingRepository, nameof(publishingResiliencePolicies.PublishedFundingRepository));
            Guard.ArgumentNotNull(publishingResiliencePolicies.JobsApiClient, nameof(publishingResiliencePolicies.JobsApiClient));
            Guard.ArgumentNotNull(publishingResiliencePolicies.PoliciesApiClient, nameof(publishingResiliencePolicies.PoliciesApiClient));
            Guard.ArgumentNotNull(jobManagement, nameof(jobManagement));

            _publishedFundingStatusUpdateService = publishedFundingStatusUpdateService;
            _publishedFundingDataService = publishedFundingDataService;
            _specificationService = specificationService;
            _organisationGroupGenerator = organisationGroupGenerator;
            _publishPrerequisiteChecker = publishPrerequisiteChecker;
            _publishedFundingChangeDetectorService = publishedFundingChangeDetectorService;
            _publishedFundingGenerator = publishedFundingGenerator;
            _publishedProviderDataGenerator = publishedProviderDataGenerator;
            _publishedProviderContentsGeneratorResolver = publishedProviderContentsGeneratorResolver;
            _publishedFundingContentsPersistanceService = publishedFundingContentsPersistanceService;
            _publishedProviderContentsPersistanceService = publishedProviderContentsPersistanceService;
            _calculationResultsService = calculationResultsService;
            _publishedFundingDateService = publishedFundingDateService;
            _publishedProviderStatusUpdateService = publishedProviderStatusUpdateService;
            _providerService = providerService;
            _publishedFundingSearchRepository = publishedFundingSearchRepository;
            _publishedProviderIndexerService = publishedProviderIndexerService;
            _jobsApiClient = jobsApiClient;
            _policiesApiClient = policiesApiClient;
            _logger = logger;
            _publishingEngineOptions = publishingEngineOptions;
            _calculationsApiClient = calculationsApiClient;

            _publishingResiliencePolicy = publishingResiliencePolicies.PublishedFundingRepository;
            _jobsApiClientPolicy = publishingResiliencePolicies.JobsApiClient;
            _calculationsApiClientPolicy = publishingResiliencePolicies.CalculationsApiClient;
            _policyApiClientPolicy = publishingResiliencePolicies.PoliciesApiClient;
            _jobManagement = jobManagement;
            _profilingService = profilingService;
        }

        public async Task PublishResults(Message message)
        {
            Guard.ArgumentNotNull(message, nameof(message));

            _logger.Information("Starting PublishFunding job");

            Reference author = message.GetUserDetails();

            string specificationId = message.UserProperties["specification-id"] as string;
            string jobId = message.UserProperties["jobId"]?.ToString();

            JobViewModel currentJob;
            try
            {
                currentJob = await _jobManagement.RetrieveJobAndCheckCanBeProcessed(jobId);
            }
            catch (Exception e)
            {
                string errorMessage = "Job can not be run";
                _logger.Error(errorMessage);

                throw new NonRetriableException(errorMessage);
            }

            // Update job to set status to processing
            await _jobManagement.UpdateJobStatus(jobId, 0, 0, null, null);

            SpecificationSummary specification = await _specificationService.GetSpecificationSummaryById(specificationId);

            if (specification == null)
            {
                throw new NonRetriableException($"Could not find specification with id '{specificationId}'");
            }

            FundingPeriod fundingPeriod = await GetFundingPeriod(specification);

            PublishedFundingDates publishingDates = await _publishedFundingDateService.GetDatesForSpecification(specificationId);

            foreach (Reference fundingStream in specification.FundingStreams)
            {
                await PublishFundingStream(fundingStream, specificationId, specification, jobId, fundingPeriod, publishingDates, author);
            }

            _logger.Information($"Running search reindexer for published funding");
            await _publishedFundingSearchRepository.RunIndexer();

            // Mark job as complete
            _logger.Information($"Marking publish funding job complete");

            await _jobManagement.UpdateJobStatus(jobId, 0, 0, true, null);
            _logger.Information($"Publish funding job complete");
        }

        private async Task<FundingPeriod> GetFundingPeriod(SpecificationSummary specification)
        {
            ApiResponse<FundingPeriod> fundingPeriod =
                await _policyApiClientPolicy.ExecuteAsync(() => _policiesApiClient.GetFundingPeriodById(specification.FundingPeriod.Id));
            if (fundingPeriod.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Unable to lookup funding period from policy service");
            }

            if (fundingPeriod.Content == null)
            {
                throw new Exception("Unable to lookup funding period from policy service - content null");
            }

            return fundingPeriod.Content;
        }

        private async Task PublishFundingStream(Reference fundingStream,
            string specificationId,
            SpecificationSummary specification,
            string jobId,
            FundingPeriod fundingPeriod,
            PublishedFundingDates publishingDates,
            Reference author)
        {
            _logger.Information($"Processing Publish Funding for {fundingStream.Id} in specification {specificationId}");

            if (!specification.TemplateIds.ContainsKey(fundingStream.Id) || string.IsNullOrWhiteSpace(specification.TemplateIds[fundingStream.Id]))
            {
                _logger.Information($"Skipped publishing {fundingStream.Id} as no template exists");

                return;
            }

            List<PublishedProvider> publishedProvidersForFundingStream = await GetPublishedProvidersForFundingStream(fundingStream, specificationId, specification);

            _logger.Information($"Verifying prerequisites for funding publish");

            await CheckPrerequisitesForSpecificationToBePublished(specificationId, specification, jobId, publishedProvidersForFundingStream);

            _logger.Information($"Prerequisites for publish passed");

            _logger.Information($"Fetching existing published funding");
            // Get latest version of existing published funding
            IEnumerable<PublishedFunding> publishedFunding = await _publishingResiliencePolicy.ExecuteAsync(() =>
                _publishedFundingDataService.GetCurrentPublishedFunding(fundingStream.Id, specification.FundingPeriod.Id));

            _logger.Information($"Fetched {publishedFunding.Count()} existing published funding items");

            List<PublishedProvider> publishedProviders = publishedProvidersForFundingStream.Where(p => p.Current.FundingStreamId == fundingStream.Id).ToList();

            TemplateMetadataContents templateMetadataContents = await ReadTemplateMetadataContents(fundingStream, specification);

            FundingConfiguration fundingConfiguration = await ReadFundingConfiguration(fundingStream, specification);

            Dictionary<string, Provider> scopedProviders = await ReadScopedProviders(specificationId, specification, publishedProviders);

            // Get calculation results for specification 
            _logger.Information($"Looking up calculation results");

            IDictionary<string, ProviderCalculationResult> allCalculationResults;
            try
            {
                allCalculationResults = await _calculationResultsService.GetCalculationResultsBySpecificationId(specificationId, scopedProviders.Keys);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during calculation result lookup");
                throw;
            }

            _logger.Information($"Found calculation results for {allCalculationResults.Count} providers from cosmos for publish job");

            TemplateMapping templateMapping = await GetTemplateMapping(fundingStream, specificationId);

            _logger.Information("Generating PublishedProviders for publish");

            // Generate populated data for each provider in this funding line
            Dictionary<string, GeneratedProviderResult> generatedPublishedProviderData;
            try
            {
                generatedPublishedProviderData =
                    _publishedProviderDataGenerator.Generate(templateMetadataContents, templateMapping, scopedProviders.Values, allCalculationResults);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during generating provider data");
                throw;
            }

            // Profile payment funding lines
            try
            {
                await _profilingService.ProfileFundingLines(generatedPublishedProviderData.SelectMany(c => c.Value.FundingLines.Where(f => f.Type == OrganisationGroupingReason.Payment)), fundingStream.Id, specification.FundingPeriod.Id);
            }
            catch (Exception ex)
            {

                _logger.Error(ex, "Exception during generating provider profiling");
                throw;
            }

            _logger.Information("Populated PublishedProviders for publish");

            _logger.Information($"Generating organisation groups");

            // Foreach group, determine the provider versions required to be latest
            IEnumerable<OrganisationGroupResult> organisationGroups =
                await _organisationGroupGenerator.GenerateOrganisationGroup(fundingConfiguration, scopedProviders.Values, specification.ProviderVersionId);

            _logger.Information($"A total of {organisationGroups.Count()} were generated");

            _logger.Information($"Generating organisation groups to save");

            // Compare existing published provider versions with existing current PublishedFundingVersion
            IEnumerable<(PublishedFunding PublishedFunding, OrganisationGroupResult OrganisationGroupResult)> organisationGroupsToSave =
                _publishedFundingChangeDetectorService.GenerateOrganisationGroupsToSave(organisationGroups, publishedFunding, publishedProviders);

            _logger.Information($"A total of {organisationGroupsToSave.Count()} organisation groups returned to save");

            // Generate PublishedFundingVersion for new and updated PublishedFundings
            GeneratePublishedFundingInput generatePublishedFundingInput = new GeneratePublishedFundingInput()
            {
                OrganisationGroupsToSave = organisationGroupsToSave,
                TemplateMetadataContents = templateMetadataContents,
                PublishedProviders = publishedProviders,
                TemplateVersion = specification.TemplateIds[fundingStream.Id],
                FundingStream = fundingStream,
                FundingPeriod = fundingPeriod,
                PublishingDates = publishingDates,
                SpecificationId = specification.Id,
            };

            List<PublishedProvider> publishedProvidersToSaveAsReleased = await SavePublishedProvidersAsPublishedReleased(jobId, author, publishedProviders);

            if (!publishedProvidersToSaveAsReleased.IsNullOrEmpty())
            {
                // Update Published Providers in search 
                _logger.Information($"Updating published providers in search. Total='{publishedProvidersToSaveAsReleased.Count}'");
                await _publishedProviderIndexerService.IndexPublishedProviders(publishedProvidersToSaveAsReleased.Select(_ => _.Current));
                _logger.Information($"Finished updating published providers in search");
            }

            _logger.Information($"Generating published funding");
            IEnumerable<(PublishedFunding PublishedFunding, PublishedFundingVersion PublishedFundingVersion)> publishedFundingToSave =
                _publishedFundingGenerator.GeneratePublishedFunding(generatePublishedFundingInput).ToList();
            _logger.Information($"A total of {publishedFundingToSave.Count()} published funding versions created to save.");

            // Save a version of published funding and set this version to current
            _logger.Information($"Saving published funding");
            await _publishedFundingStatusUpdateService.UpdatePublishedFundingStatus(publishedFundingToSave, author, PublishedFundingStatus.Released);
            _logger.Information($"Finished saving published funding");

            // Save contents to blob storage and search for the feed
            _logger.Information($"Saving published funding contents");
            await _publishedFundingContentsPersistanceService.SavePublishedFundingContents(publishedFundingToSave.Select(_ => _.PublishedFundingVersion),
                templateMetadataContents);
            _logger.Information($"Finished saving published funding contents");

            if (!publishedProvidersToSaveAsReleased.IsNullOrEmpty())
            {
                // Generate contents JSON for provider and save to blob storage
                IPublishedProviderContentsGenerator generator = _publishedProviderContentsGeneratorResolver.GetService(templateMetadataContents.SchemaVersion);
                await _publishedProviderContentsPersistanceService.SavePublishedProviderContents(templateMetadataContents, templateMapping, generatedPublishedProviderData,
                    publishedProvidersToSaveAsReleased, generator);
            }
        }

        private async Task<List<PublishedProvider>> SavePublishedProvidersAsPublishedReleased(string jobId, Reference author, List<PublishedProvider> publishedProviders)
        {
            List<PublishedProvider> publishedProvidersToSaveAsReleased = 
                new List<PublishedProvider>(publishedProviders.Where(p => p.Current.Status != PublishedProviderStatus.Released));

            _logger.Information($"Saving published providers. Total = '{publishedProvidersToSaveAsReleased.Count()}'");

            await _publishedProviderStatusUpdateService.UpdatePublishedProviderStatus(publishedProvidersToSaveAsReleased, author, PublishedProviderStatus.Released,
                jobId);

            _logger.Information($"Finished saving published funding contents");

            return publishedProvidersToSaveAsReleased;
        }

        private async Task<TemplateMapping> GetTemplateMapping(Reference fundingStream, string specificationId)
        {
            ApiResponse<TemplateMapping> calculationMappingResult =
                await _calculationsApiClientPolicy.ExecuteAsync(() => _calculationsApiClient.GetTemplateMapping(specificationId, fundingStream.Id));

            if (calculationMappingResult == null)
            {
                throw new Exception($"calculationMappingResult returned null for funding stream {fundingStream.Id}");
            }

            return calculationMappingResult.Content;
        }

        private async Task<Dictionary<string, Provider>> ReadScopedProviders(string specificationId, 
            SpecificationSummary specification, 
            List<PublishedProvider> publishedProviders)
        {
            IEnumerable<Provider> scopedProvidersUnfiltered =
                await _providerService.GetScopedProvidersForSpecification(specificationId, specification.ProviderVersionId);

            List<string> publishedProviderProviderIds = publishedProviders.Select(p => p.Current.ProviderId).Distinct().ToList();

            // Filter scoped providers based on the PublishedProvider's which exist to support excluded PublishedProviders
            IEnumerable<Provider> scopedProvidersFiltered =
                scopedProvidersUnfiltered.Where(p => publishedProviderProviderIds.Contains(p.ProviderId));

            Dictionary<string, Provider> scopedProviders = new Dictionary<string, Provider>();
            foreach (Provider provider in scopedProvidersFiltered)
            {
                scopedProviders.Add(provider.ProviderId, provider);
            }

            return scopedProviders;
        }

        private async Task<FundingConfiguration> ReadFundingConfiguration(Reference fundingStream, SpecificationSummary specification)
        {
            // Look up the funding configuration to determine which groups to publish
            ApiResponse<FundingConfiguration> fundingConfigurationResponse = await _policyApiClientPolicy.ExecuteAsync(() => 
                _policiesApiClient.GetFundingConfiguration(fundingStream.Id, specification.FundingPeriod.Id));

            if (fundingConfigurationResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"Unable to get funding configuration for funding stream '{fundingStream.Id}'");
            }

            return fundingConfigurationResponse.Content;
        }

        private async Task<TemplateMetadataContents> ReadTemplateMetadataContents(Reference fundingStream, SpecificationSummary specification)
        {
            ApiResponse<TemplateMetadataContents> templateMetadataContentsResponse = 
                await _policiesApiClient.GetFundingTemplateContents(fundingStream.Id, specification.TemplateIds[fundingStream.Id]);

            if (templateMetadataContentsResponse?.Content == null)
            {
                throw new NonRetriableException($"Unable to get template metadata contents for funding stream. '{fundingStream.Id}'");
            }

            return templateMetadataContentsResponse.Content;
        }

        private async Task CheckPrerequisitesForSpecificationToBePublished(string specificationId, SpecificationSummary specification, string jobId,
            List<PublishedProvider> publishedProvidersForFundingStream)
        {
            IEnumerable<string> prereqValidationErrors = await _publishPrerequisiteChecker
                .PerformPrerequisiteChecks(specification, publishedProvidersForFundingStream);
            if (!prereqValidationErrors.IsNullOrEmpty())
            {
                string errorMessage = $"Specification with id: '{specificationId} has prerequisites which aren't complete.";

                await _jobManagement.UpdateJobStatus(jobId, completedSuccessfully: false, outcome: string.Join(", ", prereqValidationErrors));

                throw new NonRetriableException(errorMessage);
            }
        }

        private async Task<List<PublishedProvider>> GetPublishedProvidersForFundingStream(Reference fundingStream, string specificationId, SpecificationSummary specification)
        {
            _logger.Information($"Retrieving published provider results for {fundingStream.Id} in specification {specificationId}");

            IEnumerable<PublishedProvider> publishedProvidersResult =
                await _publishedFundingDataService.GetCurrentPublishedProviders(fundingStream.Id, specification.FundingPeriod.Id);

            // Ensure linq query evaluates only once
            List<PublishedProvider> publishedProvidersForFundingStream = new List<PublishedProvider>(publishedProvidersResult);
            _logger.Information($"Retrieved {publishedProvidersForFundingStream.Count} published provider results for {fundingStream.Id}");


            if (publishedProvidersForFundingStream.IsNullOrEmpty())
                throw new RetriableException($"Null or empty published providers returned for specification id : '{specificationId}' when setting status to released");

            return publishedProvidersForFundingStream;
        }
    }
}