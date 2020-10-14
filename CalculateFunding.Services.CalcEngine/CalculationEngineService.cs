﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.ApiClient.Results;
using CalculateFunding.Common.ApiClient.Specifications;
using CalculateFunding.Common.ApiClient.Specifications.Models;
using CalculateFunding.Common.Caching;
using CalculateFunding.Common.JobManagement;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.ServiceBus.Interfaces;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Aggregations;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Datasets;
using CalculateFunding.Models.ProviderLegacy;
using CalculateFunding.Services.CalcEngine.Interfaces;
using CalculateFunding.Services.CodeGeneration.VisualBasic;
using CalculateFunding.Services.Core;
using CalculateFunding.Services.Core.Caching;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Core.Interfaces.Logging;
using CalculateFunding.Services.Core.Options;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Polly;
using Serilog;
using AggregatedType = CalculateFunding.Models.Aggregations.AggregatedType;

namespace CalculateFunding.Services.CalcEngine
{
    public class CalculationEngineService : ICalculationEngineService
    {
        private readonly ILogger _logger;
        private readonly ICalculationEngine _calculationEngine;
        private readonly ICacheProvider _cacheProvider;
        private readonly IMessengerService _messengerService;
        private readonly IProviderSourceDatasetsRepository _providerSourceDatasetsRepository;
        private readonly ITelemetry _telemetry;
        private readonly IProviderResultsRepository _providerResultsRepository;
        private readonly ICalculationsRepository _calculationsRepository;
        private readonly ISpecificationsApiClient _specificationsApiClient;
        private readonly EngineSettings _engineSettings;
        private readonly AsyncPolicy _cacheProviderPolicy;
        private readonly AsyncPolicy _messengerServicePolicy;
        private readonly AsyncPolicy _providerSourceDatasetsRepositoryPolicy;
        private readonly AsyncPolicy _providerResultsRepositoryPolicy;
        private readonly AsyncPolicy _calculationsRepositoryPolicy;
        private readonly AsyncPolicy _specificationsApiPolicy;
        private readonly AsyncPolicy _resultsApiClientPolicy;
        private readonly IDatasetAggregationsRepository _datasetAggregationsRepository;
        private readonly IJobManagement _jobManagement;
        private readonly ICalculationEngineServiceValidator _calculationEngineServiceValidator;
        private readonly IResultsApiClient _resultsApiClient;
        private readonly IMapper _mapper;
        private readonly ISpecificationAssemblyProvider _specificationAssemblies;

        public CalculationEngineService(
            ILogger logger,
            ICalculationEngine calculationEngine,
            ICacheProvider cacheProvider,
            IMessengerService messengerService,
            IProviderSourceDatasetsRepository providerSourceDatasetsRepository,
            ITelemetry telemetry,
            IProviderResultsRepository providerResultsRepository,
            ICalculationsRepository calculationsRepository,
            EngineSettings engineSettings,
            ICalculatorResiliencePolicies resiliencePolicies,
            IDatasetAggregationsRepository datasetAggregationsRepository,
            IJobManagement jobManagement,
            ISpecificationsApiClient specificationsApiClient,
            IResultsApiClient resultsApiClient,
            IValidator<ICalculatorResiliencePolicies> calculatorResiliencePoliciesValidator,
            ICalculationEngineServiceValidator calculationEngineServiceValidator,
            IMapper mapper,
            ISpecificationAssemblyProvider specificationAssemblies)
        {
            Guard.ArgumentNotNull(logger, nameof(logger));
            Guard.ArgumentNotNull(calculationEngine, nameof(calculationEngine));
            Guard.ArgumentNotNull(cacheProvider, nameof(cacheProvider));
            Guard.ArgumentNotNull(messengerService, nameof(messengerService));
            Guard.ArgumentNotNull(providerSourceDatasetsRepository, nameof(providerSourceDatasetsRepository));
            Guard.ArgumentNotNull(telemetry, nameof(telemetry));
            Guard.ArgumentNotNull(providerResultsRepository, nameof(providerResultsRepository));
            Guard.ArgumentNotNull(calculationsRepository, nameof(calculationsRepository));
            Guard.ArgumentNotNull(engineSettings, nameof(engineSettings));
            Guard.ArgumentNotNull(resiliencePolicies?.SpecificationsApiClient, nameof(resiliencePolicies.SpecificationsApiClient));
            Guard.ArgumentNotNull(resiliencePolicies?.CacheProvider, nameof(resiliencePolicies.CacheProvider));
            Guard.ArgumentNotNull(resiliencePolicies?.Messenger, nameof(resiliencePolicies.Messenger));
            Guard.ArgumentNotNull(resiliencePolicies?.ProviderSourceDatasetsRepository, nameof(resiliencePolicies.ProviderSourceDatasetsRepository));
            Guard.ArgumentNotNull(resiliencePolicies?.ProviderResultsRepository, nameof(resiliencePolicies.ProviderResultsRepository));
            Guard.ArgumentNotNull(resiliencePolicies?.CalculationsRepository, nameof(resiliencePolicies.CalculationsRepository));
            Guard.ArgumentNotNull(resiliencePolicies?.ResultsApiClient, nameof(resiliencePolicies.ResultsApiClient));
            Guard.ArgumentNotNull(datasetAggregationsRepository, nameof(datasetAggregationsRepository));
            Guard.ArgumentNotNull(jobManagement, nameof(jobManagement));
            Guard.ArgumentNotNull(specificationsApiClient, nameof(specificationsApiClient));
            Guard.ArgumentNotNull(calculatorResiliencePoliciesValidator, nameof(calculatorResiliencePoliciesValidator));
            Guard.ArgumentNotNull(calculationEngineServiceValidator, nameof(calculationEngineServiceValidator));
            Guard.ArgumentNotNull(resultsApiClient, nameof(resultsApiClient));
            Guard.ArgumentNotNull(mapper, nameof(mapper));
            Guard.ArgumentNotNull(specificationAssemblies, nameof(specificationAssemblies));

            _calculationEngineServiceValidator = calculationEngineServiceValidator;
            _logger = logger;
            _calculationEngine = calculationEngine;
            _cacheProvider = cacheProvider;
            _messengerService = messengerService;
            _providerSourceDatasetsRepository = providerSourceDatasetsRepository;
            _telemetry = telemetry;
            _providerResultsRepository = providerResultsRepository;
            _calculationsRepository = calculationsRepository;
            _engineSettings = engineSettings;
            _cacheProviderPolicy = resiliencePolicies.CacheProvider;
            _messengerServicePolicy = resiliencePolicies.Messenger;
            _providerSourceDatasetsRepositoryPolicy = resiliencePolicies.ProviderSourceDatasetsRepository;
            _providerResultsRepositoryPolicy = resiliencePolicies.ProviderResultsRepository;
            _calculationsRepositoryPolicy = resiliencePolicies.CalculationsRepository;
            _datasetAggregationsRepository = datasetAggregationsRepository;
            _jobManagement = jobManagement;
            _specificationsApiClient = specificationsApiClient;
            _specificationsApiPolicy = resiliencePolicies.SpecificationsApiClient;
            _resultsApiClientPolicy = resiliencePolicies.ResultsApiClient;
            _resultsApiClient = resultsApiClient;
            _mapper = mapper;
            _specificationAssemblies = specificationAssemblies;
        }

        public async Task<IActionResult> GenerateAllocations(HttpRequest request)
        {
            string json = GetMessage();

            byte[] body = Encoding.ASCII.GetBytes(json);

            IDictionary<string, object> properties = new Dictionary<string, object>
            {
                { "sfa-correlationId", Guid.NewGuid().ToString() },
                { "provider-summaries-partition-size", 1000 },
                { "provider-summaries-partition-index", 5000 },
                { "provider-cache-key", "add key here" },
                { "specification-id", "add spec id here" }
            };

            Message message = new Message(body)
            {
                PartitionKey = Guid.NewGuid().ToString()
            };

            foreach (KeyValuePair<string, object> property in properties)
            {
                message.UserProperties.Add(property.Key, property.Value);
            }

            await GenerateAllocations(message);

            return new NoContentResult();
        }

        public string GetMessage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Copy message here from dead letter");
            return sb.ToString();
        }

        public async Task GenerateAllocations(Message message)
        {
            Guard.ArgumentNotNull(message, nameof(message));

            _logger.Debug("Validating new allocations message");

            _calculationEngineServiceValidator.ValidateMessage(_logger, message);

            GenerateAllocationMessageProperties messageProperties = GetMessageProperties(message);

            JobViewModel job = await AddStartingProcessJobLog(messageProperties.JobId);

            if (job == null)
            {
                return;
            }

            string specificationId = messageProperties.SpecificationId;

            messageProperties.GenerateCalculationAggregationsOnly = job.JobDefinitionId == JobConstants.DefinitionNames.GenerateCalculationAggregationsJob;

            _logger.Information($"Generating allocations for specification id {specificationId}");

            Task<(byte[], long)> getAssemblyTask = GetAssemblyForSpecification(specificationId, messageProperties.AssemblyETag);
            Task<(IEnumerable<ProviderSummary>, long)> providerSummaryTask = GetProviderSummaries(messageProperties);
            Task<(IEnumerable<CalculationSummaryModel>, long)> calculationSummaryTask = GetCalculationSummaries(specificationId);
            Task<(SpecificationSummary, long)> specificationSummaryTask = GetSpecificationSummary(specificationId);
            Task<(IEnumerable<CalculationAggregation>, long)> aggregationsTask = BuildAggregations(messageProperties);


            await TaskHelper.WhenAllAndThrow(getAssemblyTask, providerSummaryTask, calculationSummaryTask, specificationSummaryTask, aggregationsTask);

            (byte[] assembly, long assemblyLookupElapsedMilliseconds) = getAssemblyTask.Result;
            (IEnumerable<ProviderSummary> summaries, long providerSummaryLookupElapsedMilliseconds) = providerSummaryTask.Result;
            (IEnumerable<CalculationSummaryModel> calculations, long calculationsLookupStopwatchElapsedMilliseconds) = calculationSummaryTask.Result;
            (SpecificationSummary specificationSummary, long specificationSummaryElapsedMilliseconds) = specificationSummaryTask.Result;
            (IEnumerable<CalculationAggregation> aggregations, long aggregationsElapsedMilliseconds) = aggregationsTask.Result;

            int providerBatchSize = _engineSettings.ProviderBatchSize;

            IEnumerable<string> dataRelationshipIds = specificationSummary.DataDefinitionRelationshipIds;
            if (dataRelationshipIds == null)
            {
                throw new InvalidOperationException("Data relationship ids returned null");
            }

            int totalProviderResults = 0;
            bool calculationResultsHaveExceptions = false;

            Dictionary<string, List<object>> cachedCalculationAggregationsBatch = CreateCalculationAggregateBatchDictionary(messageProperties);

            for (int i = 0; i < summaries.Count(); i += providerBatchSize)
            {
                Stopwatch calculationStopwatch = new Stopwatch();
                Stopwatch providerSourceDatasetsStopwatch = new Stopwatch();

                Stopwatch calcTiming = Stopwatch.StartNew();

                CalculationResultsModel calculationResults = await CalculateResults(specificationId,
                    summaries,
                    calculations,
                    aggregations,
                    dataRelationshipIds,
                    assembly,
                    messageProperties,
                    providerBatchSize,
                    i,
                    providerSourceDatasetsStopwatch,
                    calculationStopwatch);

                _logger.Information($"Calculating results complete for specification id {specificationId}");

                long saveCosmosElapsedMs = -1;
                long saveSearchElapsedMs = -1;
                long saveRedisElapsedMs = 0;
                long? saveQueueElapsedMs = null;
                int savedProviders = 0;
                int percentageProvidersSaved = 0;

                if (calculationResults.ProviderResults.Any())
                {
                    if (messageProperties.GenerateCalculationAggregationsOnly)
                    {
                        PopulateCachedCalculationAggregationsBatch(calculationResults.ProviderResults, cachedCalculationAggregationsBatch, messageProperties);
                    }
                    else
                    {
                        (long saveCosmosElapsedMs, long saveSearchElapsedMs, long saveRedisElapsedMs, long? saveQueueElapsedMs, int savedProviders) processResultsMetrics =
                            await ProcessProviderResults(calculationResults.ProviderResults, specificationSummary, messageProperties, message);

                        saveCosmosElapsedMs = processResultsMetrics.saveCosmosElapsedMs;
                        saveSearchElapsedMs = processResultsMetrics.saveSearchElapsedMs;
                        saveRedisElapsedMs = processResultsMetrics.saveRedisElapsedMs;
                        saveQueueElapsedMs = processResultsMetrics.saveQueueElapsedMs;
                        savedProviders = processResultsMetrics.savedProviders;

                        totalProviderResults += calculationResults.ProviderResults.Count();
                        percentageProvidersSaved = savedProviders / totalProviderResults * 100;

                        if (calculationResults.ResultsContainExceptions)
                        {
                            _logger.Warning($"Exception(s) executing specification id '{specificationId}:  {calculationResults.ExceptionMessages}");
                            calculationResultsHaveExceptions = true;
                        }
                    }
                }

                calcTiming.Stop();

                IDictionary<string, double> metrics = new Dictionary<string, double>()
                {
                    { "calculation-run-providersProcessed", calculationResults.PartitionedSummaries.Count() },
                    { "calculation-run-lookupCalculationDefinitionsMs", calculationsLookupStopwatchElapsedMilliseconds },
                    { "calculation-run-providersResultsFromCache", summaries.Count() },
                    { "calculation-run-partitionSize", messageProperties.PartitionSize },
                    { "calculation-run-providerSourceDatasetQueryMs", providerSourceDatasetsStopwatch.ElapsedMilliseconds },
                    { "calculation-run-saveProviderResultsRedisMs", saveRedisElapsedMs },
                    { "calculation-run-runningCalculationMs",  calculationStopwatch.ElapsedMilliseconds },
                    { "calculation-run-savedProviders",  savedProviders },
                    { "calculation-run-savePercentage ",  percentageProvidersSaved },
                    { "calculation-run-specLookup ",  specificationSummaryElapsedMilliseconds },
                    { "calculation-run-providerSummaryLookup ",  providerSummaryLookupElapsedMilliseconds },
                    { "calculation-run-assemblyLookup ",  assemblyLookupElapsedMilliseconds },
                    { "calculation-run-aggregationsLookup ",  aggregationsElapsedMilliseconds },
                };

                if (saveQueueElapsedMs.HasValue)
                {
                    metrics.Add("calculation-run-saveProviderResultsServiceBusMs", saveQueueElapsedMs.Value);
                }

                if (saveCosmosElapsedMs > -1)
                {
                    metrics.Add("calculation-run-elapsedMilliseconds", calcTiming.ElapsedMilliseconds);
                    metrics.Add("calculation-run-saveProviderResultsCosmosMs", saveCosmosElapsedMs);
                    metrics.Add("calculation-run-saveProviderResultsSearchMs", saveSearchElapsedMs);
                }
                else
                {
                    metrics.Add("calculation-run-for-tests-ms", calcTiming.ElapsedMilliseconds);
                }

                _telemetry.TrackEvent("CalculationRunProvidersProcessed",
                    new Dictionary<string, string>()
                    {
                    { "specificationId" , specificationId },
                    },
                    metrics
                );
            }

            if (calculationResultsHaveExceptions)
            {
                await FailJob(messageProperties.JobId, totalProviderResults, "Exceptions were thrown during generation of calculation results");
            }
            else
            {
                await CompleteBatch(specificationSummary, messageProperties, cachedCalculationAggregationsBatch, summaries.Count(), totalProviderResults);
            }
        }

        private async Task<(SpecificationSummary, long)> GetSpecificationSummary(string specificationId)
        {
            Stopwatch specsLookupStopwatch = Stopwatch.StartNew();
            ApiResponse<SpecificationSummary> specificationQuery = await _specificationsApiClient.GetSpecificationSummaryById(specificationId);
            if (specificationQuery == null || specificationQuery.StatusCode != HttpStatusCode.OK || specificationQuery.Content == null)
            {
                throw new InvalidOperationException("Specification summary is null");
            }

            specsLookupStopwatch.Stop();

            return (specificationQuery.Content, specsLookupStopwatch.ElapsedMilliseconds);
        }

        private async Task<(IEnumerable<CalculationSummaryModel>, long)> GetCalculationSummaries(string specificationId)
        {
            Stopwatch calculationsLookupStopwatch = Stopwatch.StartNew();
            IEnumerable<CalculationSummaryModel> calculations = await _calculationsRepositoryPolicy.ExecuteAsync(() =>
                _calculationsRepository.GetCalculationSummariesForSpecification(specificationId));

            if (calculations == null)
            {
                _logger.Error($"Calculations lookup API returned null for specification id {specificationId}");

                throw new InvalidOperationException("Calculations lookup API returned null");
            }
            calculationsLookupStopwatch.Stop();

            return (calculations, calculationsLookupStopwatch.ElapsedMilliseconds);
        }

        private async Task<(IEnumerable<ProviderSummary>, long)> GetProviderSummaries(GenerateAllocationMessageProperties messageProperties)
        {
            _logger.Information($"processing partition index {messageProperties.PartitionIndex} for batch size {messageProperties.PartitionSize}");

            int start = messageProperties.PartitionIndex;

            int stop = start + messageProperties.PartitionSize - 1;

            Stopwatch providerSummaryLookup = Stopwatch.StartNew();
            IEnumerable<ProviderSummary> summaries = await _cacheProviderPolicy.ExecuteAsync(() => _cacheProvider.ListRangeAsync<ProviderSummary>(messageProperties.ProviderCacheKey, start, stop));
            if (summaries == null)
            {
                throw new InvalidOperationException("Null provider summaries returned");
            }

            if (!summaries.Any())
            {
                throw new InvalidOperationException("No provider summaries returned to process");
            }

            providerSummaryLookup.Stop();

            return (summaries, providerSummaryLookup.ElapsedMilliseconds);
        }

        private async Task<(byte[], long)> GetAssemblyForSpecification(string specificationId, string etag)
        {
            Stopwatch assemblyLookupStopwatch = Stopwatch.StartNew();

            if (etag.IsNotNullOrWhitespace())
            {
                Stream cachedAssembly = await _specificationAssemblies.GetAssembly(specificationId, etag);
                
                if (cachedAssembly != null)
                {
                    assemblyLookupStopwatch.Stop();
                    
                    return (cachedAssembly.ReadAllBytes(), assemblyLookupStopwatch.ElapsedMilliseconds);
                }
            }
            
            byte[] assembly = await _calculationsRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetAssemblyBySpecificationId(specificationId));

            if (assembly == null)
            {
                string error = $"Failed to get assembly for specification Id '{specificationId}'";
                
                _logger.Error(error);
                
                throw new RetriableException(error);
            }

            await _specificationAssemblies.SetAssembly(specificationId, new MemoryStream(assembly));

            assemblyLookupStopwatch.Stop();

            return (assembly, assemblyLookupStopwatch.ElapsedMilliseconds);
        }

        private async Task<CalculationResultsModel> CalculateResults(string specificationId, IEnumerable<ProviderSummary> summaries,
            IEnumerable<CalculationSummaryModel> calculations,
            IEnumerable<CalculationAggregation> aggregations,
            IEnumerable<string> dataRelationshipIds,
            byte[] assemblyForSpecification,
            GenerateAllocationMessageProperties messageProperties,
            int providerBatchSize,
            int index,
            Stopwatch providerSourceDatasetsStopwatch,
            Stopwatch calculationStopwatch)
        {
            ConcurrentBag<ProviderResult> providerResults = new ConcurrentBag<ProviderResult>();

            Guard.ArgumentNotNull(summaries, nameof(summaries));

            IEnumerable<ProviderSummary> partitionedSummaries = summaries.Skip(index).Take(providerBatchSize);

            IList<string> providerIdList = partitionedSummaries.Select(m => m.Id).ToList();

            providerSourceDatasetsStopwatch.Start();

            _logger.Information($"Fetching provider sources for specification id {messageProperties.SpecificationId}");

            IDictionary<string, IEnumerable<ProviderSourceDataset>> providerSourceDatasetsByProvider = await _providerSourceDatasetsRepositoryPolicy.ExecuteAsync(
                () => _providerSourceDatasetsRepository.GetProviderSourceDatasetsByProviderIdsAndRelationshipIds(specificationId, providerIdList, dataRelationshipIds));

            providerSourceDatasetsStopwatch.Stop();

            _logger.Information($"Fetched provider sources found for specification id {messageProperties.SpecificationId}");

            calculationStopwatch.Start();

            _logger.Information($"Calculating results for specification id {messageProperties.SpecificationId}");

            Assembly assembly = Assembly.Load(assemblyForSpecification);

            Parallel.ForEach(partitionedSummaries, new ParallelOptions { MaxDegreeOfParallelism = _engineSettings.CalculateProviderResultsDegreeOfParallelism }, provider =>
            {
                if (provider == null)
                {
                    throw new Exception("Provider summary is null");
                }

                IAllocationModel allocationModel = _calculationEngine.GenerateAllocationModel(assembly);

                IEnumerable<ProviderSourceDataset> providerDatasets = Enumerable.Empty<ProviderSourceDataset>();

                if (!providerSourceDatasetsByProvider.TryGetValue(provider.Id, out providerDatasets))
                {
                    throw new Exception($"Provider source dataset not found for {provider.Id}.");
                }

                ProviderResult result = _calculationEngine.CalculateProviderResults(allocationModel, specificationId, calculations, provider, providerDatasets, aggregations);

                if (result == null)
                {
                    throw new InvalidOperationException("Null result from Calc Engine CalculateProviderResults");
                }

                providerResults.Add(result);
            });


            _logger.Information($"Calculating results complete for specification id {messageProperties.SpecificationId}");

            calculationStopwatch.Stop();

            return new CalculationResultsModel
            {
                ProviderResults = providerResults,
                PartitionedSummaries = partitionedSummaries
            };
        }

        private GenerateAllocationMessageProperties GetMessageProperties(Message message)
        {
            GenerateAllocationMessageProperties properties = new GenerateAllocationMessageProperties();

            if (message.UserProperties.ContainsKey("jobId"))
            {
                properties.JobId = message.UserProperties["jobId"].ToString();
            }
            else
            {
                _logger.Error("Missing job id for generating allocations");
            }

            string specificationId = message.UserProperties["specification-id"].ToString();

            properties.SpecificationId = specificationId;

            int batchNumber = 0;

            if (message.UserProperties.ContainsKey("batch-number"))
            {
                batchNumber = int.Parse(message.UserProperties["batch-number"].ToString());
            }

            int batchCount = 0;

            if (message.UserProperties.ContainsKey("batch-count"))
            {
                batchCount = int.Parse(message.UserProperties["batch-count"].ToString());
            }

            properties.BatchNumber = batchNumber;

            properties.BatchCount = batchCount;

            properties.ProviderCacheKey = message.UserProperties["provider-cache-key"].ToString();

            properties.SpecificationSummaryCacheKey = message.UserProperties["specification-summary-cache-key"].ToString();

            properties.PartitionIndex = int.Parse(message.UserProperties["provider-summaries-partition-index"].ToString());

            properties.PartitionSize = int.Parse(message.UserProperties["provider-summaries-partition-size"].ToString());

            properties.CalculationsAggregationsBatchCacheKey = $"{CacheKeys.CalculationAggregations}{specificationId}_{batchNumber}";

            properties.CalculationsToAggregate = message.UserProperties.ContainsKey("calculations-to-aggregate") && !string.IsNullOrWhiteSpace(message.UserProperties["calculations-to-aggregate"].ToString()) ? message.UserProperties["calculations-to-aggregate"].ToString().Split(',') : null;


            properties.User = message.GetUserDetails();
            properties.CorrelationId = message.GetCorrelationId();

            properties.AssemblyETag = message.GetUserProperty<string>("assembly-etag");            

            return properties;
        }

        private async Task<JobViewModel> AddStartingProcessJobLog(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                _logger.Error($"No jobId given.");
                throw new NonRetriableException("No Job Id given");
            }

            JobViewModel job;

            try
            {
                job = await _jobManagement.RetrieveJobAndCheckCanBeProcessed(jobId);
            }
            catch (JobNotFoundException)
            {
                throw new NonRetriableException($"Could not find the parent job with job id: '{jobId}'");
            }
            catch (JobAlreadyCompletedException)
            {
                return null;
            }

            await _jobManagement.AddJobLog(jobId, new JobLogUpdateModel());

            return job;
        }


        private Dictionary<string, List<object>> CreateCalculationAggregateBatchDictionary(GenerateAllocationMessageProperties messageProperties)
        {
            if (!messageProperties.GenerateCalculationAggregationsOnly)
            {
                return null;
            }

            Dictionary<string, List<object>> cachedCalculationAggregationsBatch = new Dictionary<string, List<object>>(StringComparer.InvariantCultureIgnoreCase);

            if (!messageProperties.CalculationsToAggregate.IsNullOrEmpty())
            {
                foreach (string calcToAggregate in messageProperties.CalculationsToAggregate)
                {
                    if (!cachedCalculationAggregationsBatch.ContainsKey(calcToAggregate))
                    {
                        cachedCalculationAggregationsBatch.Add(calcToAggregate, new List<object>());
                    }
                }
            }

            return cachedCalculationAggregationsBatch;
        }

        private async Task<(IEnumerable<CalculationAggregation>, long)> BuildAggregations(GenerateAllocationMessageProperties messageProperties)
        {
            Stopwatch sw = Stopwatch.StartNew();
            IEnumerable<CalculationAggregation> aggregations = Enumerable.Empty<CalculationAggregation>();

            aggregations = await _cacheProvider.GetAsync<List<CalculationAggregation>>($"{ CacheKeys.DatasetAggregationsForSpecification}{messageProperties.SpecificationId}");

            if (aggregations.IsNullOrEmpty())
            {
                aggregations = (await _datasetAggregationsRepository.GetDatasetAggregationsForSpecificationId(messageProperties.SpecificationId)).Select(m => new CalculationAggregation
                {
                    SpecificationId = m.SpecificationId,
                    Values = m.Fields.IsNullOrEmpty() ? Enumerable.Empty<AggregateValue>() : m.Fields.Select(f => new AggregateValue
                    {
                        AggregatedType = f.FieldType,
                        FieldDefinitionName = f.FieldDefinitionName,
                        Value = f.Value
                    })
                });

                await _cacheProvider.SetAsync($"{CacheKeys.DatasetAggregationsForSpecification}{messageProperties.SpecificationId}", aggregations.ToList());
            }

            if (!messageProperties.GenerateCalculationAggregationsOnly)
            {
                Dictionary<string, List<decimal>> cachedCalculationAggregations = new Dictionary<string, List<decimal>>(StringComparer.InvariantCultureIgnoreCase);

                for (int i = 1; i <= messageProperties.BatchCount; i++)
                {
                    string batchedCacheKey = $"{CacheKeys.CalculationAggregations}{messageProperties.SpecificationId}_{i}";

                    Dictionary<string, List<decimal>> cachedCalculationAggregationsPart = await _cacheProviderPolicy.ExecuteAsync(() => _cacheProvider.GetAsync<Dictionary<string, List<decimal>>>(batchedCacheKey));

                    if (!cachedCalculationAggregationsPart.IsNullOrEmpty())
                    {
                        foreach (KeyValuePair<string, List<decimal>> cachedAggregations in cachedCalculationAggregationsPart)
                        {
                            if (!cachedCalculationAggregations.ContainsKey(cachedAggregations.Key))
                            {
                                cachedCalculationAggregations.Add(cachedAggregations.Key, new List<decimal>());
                            }

                            cachedCalculationAggregations[cachedAggregations.Key].AddRange(cachedAggregations.Value);
                        }
                    }
                }

                if (!cachedCalculationAggregations.IsNullOrEmpty())
                {
                    foreach (KeyValuePair<string, List<decimal>> cachedCalculationAggregation in cachedCalculationAggregations)
                    {
                        aggregations = aggregations.Concat(new[]
                        {
                                new CalculationAggregation
                                {
                                    SpecificationId = messageProperties.SpecificationId,
                                    Values = new []
                                    {
                                        new AggregateValue { FieldDefinitionName = cachedCalculationAggregation.Key, AggregatedType = AggregatedType.Sum, Value = cachedCalculationAggregation.Value.Sum()},
                                        new AggregateValue { FieldDefinitionName = cachedCalculationAggregation.Key, AggregatedType = AggregatedType.Min, Value = cachedCalculationAggregation.Value.Min()},
                                        new AggregateValue { FieldDefinitionName = cachedCalculationAggregation.Key, AggregatedType = AggregatedType.Max, Value = cachedCalculationAggregation.Value.Max()},
                                        new AggregateValue { FieldDefinitionName = cachedCalculationAggregation.Key, AggregatedType = AggregatedType.Average, Value = cachedCalculationAggregation.Value.Average()},
                                    }
                                }
                            });
                    }
                }
            }

            return (aggregations, sw.ElapsedMilliseconds);
        }

        private async Task CompleteBatch(SpecificationSummary specificationSummary,
            GenerateAllocationMessageProperties messageProperties,
            Dictionary<string, List<object>> cachedCalculationAggregationsBatch,
            int itemsProcessed,
            int totalProviderResults)
        {
            int itemsSucceeded = totalProviderResults;
            int itemsFailed = itemsProcessed - itemsSucceeded;
            string outcome = $"{itemsSucceeded} provider results were generated successfully from {itemsProcessed} providers";

            if (messageProperties.GenerateCalculationAggregationsOnly)
            {
                await _cacheProviderPolicy.ExecuteAsync(() => _cacheProvider.SetAsync(messageProperties.CalculationsAggregationsBatchCacheKey, cachedCalculationAggregationsBatch));

                outcome = $"{itemsSucceeded} provider result calculation aggregations were generated successfully from {itemsProcessed} providers";
            }

            await _jobManagement.AddJobLog(messageProperties.JobId, new JobLogUpdateModel
            {
                CompletedSuccessfully = true,
                ItemsSucceeded = itemsSucceeded,
                ItemsFailed = itemsFailed,
                ItemsProcessed = itemsProcessed,
                Outcome = outcome
            });

            await _resultsApiClientPolicy.ExecuteAsync(() => _resultsApiClient.UpdateFundingStructureLastModified(
                new Common.ApiClient.Results.Models.UpdateFundingStructureLastModifiedRequest
            {
                LastModified = DateTimeOffset.UtcNow,
                SpecificationId = messageProperties.SpecificationId,
                FundingPeriodId = specificationSummary.FundingPeriod?.Id,
                FundingStreamId = specificationSummary.FundingStreams?.FirstOrDefault().Id
            }));
        }

        private void PopulateCachedCalculationAggregationsBatch(IEnumerable<ProviderResult> providerResults, Dictionary<string, List<object>> cachedCalculationAggregationsBatch, GenerateAllocationMessageProperties messageProperties)
        {
            if (cachedCalculationAggregationsBatch == null)
            {
                _logger.Error($"Cached calculation aggregations not found for key: {messageProperties.CalculationsAggregationsBatchCacheKey}");

                throw new Exception($"Cached calculation aggregations not found for key: {messageProperties.CalculationsAggregationsBatchCacheKey}");
            }

            IEnumerable<string> calculationsToAggregate = messageProperties.CalculationsToAggregate;

            foreach (ProviderResult providerResult in providerResults)
            {
                IEnumerable<CalculationResult> calculationResultsForAggregation = providerResult.CalculationResults.Where(m =>
                    calculationsToAggregate.Contains(VisualBasicTypeGenerator.GenerateIdentifier(m.Calculation.Name), StringComparer.InvariantCultureIgnoreCase));

                foreach (CalculationResult calculationResult in calculationResultsForAggregation)
                {
                    string calculationReferenceName = CalculationTypeGenerator.GenerateIdentifier(calculationResult.Calculation.Name.Trim());

                    string calcNameFromCalcsToAggregate = messageProperties.CalculationsToAggregate.FirstOrDefault(m => string.Equals(m, calculationReferenceName, StringComparison.InvariantCultureIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(calcNameFromCalcsToAggregate) && cachedCalculationAggregationsBatch.ContainsKey(calculationReferenceName))
                    {
                        cachedCalculationAggregationsBatch[calcNameFromCalcsToAggregate].Add(calculationResult.Value ?? 0);
                    }

                }
            }
        }

        private async Task<(long saveCosmosElapsedMs, long saveToSearchElapsedMs, long saveRedisElapsedMs, long? saveQueueElapsedMs, int savedProviders)> ProcessProviderResults(
            IEnumerable<ProviderResult> providerResults,
            SpecificationSummary specificationSummary,
            GenerateAllocationMessageProperties messageProperties,
            Message message)
        {
            (long saveToCosmosElapsedMs, long saveToSearchElapsedMs, int savedProviders) saveProviderResultsTimings = (-1, -1, -1);

            if (!message.UserProperties.ContainsKey("ignore-save-provider-results"))
            {
                _logger.Information($"Saving results for specification id {messageProperties.SpecificationId}");

                saveProviderResultsTimings = await _providerResultsRepositoryPolicy.ExecuteAsync(() => _providerResultsRepository.SaveProviderResults(providerResults,
                    specificationSummary,
                    messageProperties.PartitionIndex,
                    messageProperties.PartitionSize,
                    messageProperties.User,
                    messageProperties.CorrelationId,
                    _engineSettings.SaveProviderDegreeOfParallelism));

                _logger.Information($"Saving results completeed for specification id {messageProperties.SpecificationId}");
            }
            Stopwatch saveQueueStopwatch = null;
            Stopwatch saveRedisStopwatch = null;

            if (_engineSettings.IsTestEngineEnabled)
            {
                // Should just be the GUID as the content, as the prefix is read by the receiver, rather than the sender
                string providerResultsCacheKey = Guid.NewGuid().ToString();

                _logger.Information($"Saving results to cache for specification id {messageProperties.SpecificationId} with key {providerResultsCacheKey}");

                saveRedisStopwatch = Stopwatch.StartNew();
                await _cacheProviderPolicy.ExecuteAsync(() => _cacheProvider.SetAsync($"{CacheKeys.ProviderResultBatch}{providerResultsCacheKey}", providerResults.ToList(), TimeSpan.FromHours(12), false));
                saveRedisStopwatch.Stop();

                _logger.Information($"Saved results to cache for specification id {messageProperties.SpecificationId} with key {providerResultsCacheKey}");

                IDictionary<string, string> properties = message.BuildMessageProperties();

                properties.Add("specificationId", messageProperties.SpecificationId);

                properties.Add("providerResultsCacheKey", providerResultsCacheKey);

                _logger.Information($"Sending message for test exceution for specification id {messageProperties.SpecificationId}");

                saveQueueStopwatch = Stopwatch.StartNew();
                await _messengerServicePolicy.ExecuteAsync(() => _messengerService.SendToQueue<string>(ServiceBusConstants.QueueNames.TestEngineExecuteTests, null, properties));
                saveQueueStopwatch.Stop();

                _logger.Information($"Message sent for test exceution for specification id {messageProperties.SpecificationId}");
            }

            return (saveProviderResultsTimings.saveToCosmosElapsedMs,
                saveProviderResultsTimings.saveToSearchElapsedMs,
                saveRedisStopwatch != null ? saveRedisStopwatch.ElapsedMilliseconds : 0,
                saveQueueStopwatch?.ElapsedMilliseconds,
                saveProviderResultsTimings.savedProviders);
        }

        private async Task FailJob(string jobId, int itemsProcessed, string outcome = null)
        {
            JobLogUpdateModel jobLogUpdateModel = new JobLogUpdateModel
            {
                CompletedSuccessfully = false,
                ItemsProcessed = itemsProcessed,
                Outcome = outcome
            };

            JobLog jobLog = await _jobManagement.AddJobLog(jobId, jobLogUpdateModel);

            if (jobLog == null)
            {
                _logger.Error($"Failed to add a job log for job id '{jobId}'");
            }
        }

        private void LogAndThrowException<T>(string message) where T : Exception
        {
            _logger.Error(message);
            throw (T)Activator.CreateInstance(typeof(T), message);
        }
    }
}
