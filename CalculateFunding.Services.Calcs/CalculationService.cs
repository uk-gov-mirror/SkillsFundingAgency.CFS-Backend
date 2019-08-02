﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using CalculateFunding.Common.ApiClient.Jobs;
using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Common.ApiClient.Policies;
using CalculateFunding.Common.Caching;
using CalculateFunding.Common.FeatureToggles;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.Models.HealthCheck;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Aggregations;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Code;
using CalculateFunding.Models.Datasets;
using CalculateFunding.Models.Datasets.ViewModels;
using CalculateFunding.Models.Exceptions;
using CalculateFunding.Models.Specs;
using CalculateFunding.Models.Versioning;
using CalculateFunding.Repositories.Common.Search;
using CalculateFunding.Services.Calcs.Interfaces;
using CalculateFunding.Services.Calcs.ResultModels;
using CalculateFunding.Services.CodeGeneration;
using CalculateFunding.Services.CodeGeneration.VisualBasic;
using CalculateFunding.Services.Compiler;
using CalculateFunding.Services.Core;
using CalculateFunding.Services.Core.Caching;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Core.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using Serilog;
using Calculation = CalculateFunding.Models.Calcs.Calculation;
using CalculationCurrentVersion = CalculateFunding.Models.Calcs.CalculationCurrentVersion;

namespace CalculateFunding.Services.Calcs
{
    public class CalculationService : ICalculationService, IHealthChecker
    {
        public const string reasonForCommenting = "The dataset definition referenced by this calc has been updated and subsequently the code has been commented out";
        public const string exceptionMessage = "Code commented out for definition field updates";
        public const string exceptionType = "DatasetReferenceChangeException";

        private readonly ICalculationsRepository _calculationsRepository;
        private readonly ILogger _logger;
        private readonly ISearchRepository<CalculationIndex> _searchRepository;
        private readonly IValidator<Calculation> _calculationValidator;
        private readonly IBuildProjectsService _buildProjectsService;
        private readonly ISpecificationRepository _specsRepository;
        private readonly ICacheProvider _cacheProvider;
        private readonly Polly.Policy _calculationRepositoryPolicy;
        private readonly Polly.Policy _calculationSearchRepositoryPolicy;
        private readonly Polly.Policy _cachePolicy;
        private readonly Polly.Policy _calculationVersionsRepositoryPolicy;
        private readonly Polly.Policy _specificationsRepositoryPolicy;
        private readonly Polly.Policy _jobsApiClientPolicy;
        private readonly IVersionRepository<CalculationVersion> _calculationVersionRepository;
        private readonly IJobsApiClient _jobsApiClient;
        private readonly ISourceCodeService _sourceCodeService;
        private readonly IFeatureToggle _featureToggle;
        private readonly IBuildProjectsRepository _buildProjectsRepository;
        private readonly Polly.Policy _buildProjectRepositoryPolicy;
        private readonly ICalculationCodeReferenceUpdate _calculationCodeReferenceUpdate;
        private readonly IMapper _mapper;
        private readonly IValidator<CalculationCreateModel> _calculationCreateModelValidator;
        private readonly IValidator<CalculationEditModel> _calculationEditModelValidator;

        public CalculationService(
            IMapper mapper,
            ICalculationsRepository calculationsRepository,
            ILogger logger,
            ISearchRepository<CalculationIndex> searchRepository,
            IValidator<Calculation> calculationValidator,
            IBuildProjectsService buildProjectsService,
            ISpecificationRepository specificationRepository,
            IPoliciesApiClient policiesApiClient,
            ICacheProvider cacheProvider,
            ICalcsResiliencePolicies resiliencePolicies,
            IVersionRepository<CalculationVersion> calculationVersionRepository,
            IJobsApiClient jobsApiClient,
            ISourceCodeService sourceCodeService,
            IFeatureToggle featureToggle,
            IBuildProjectsRepository buildProjectsRepository,
            ICalculationCodeReferenceUpdate calculationCodeReferenceUpdate,
            IValidator<CalculationCreateModel> calculationCreateModelValidator,
            IValidator<CalculationEditModel> calculationEditModelValidator)
        {
            Guard.ArgumentNotNull(mapper, nameof(mapper));
            Guard.ArgumentNotNull(calculationsRepository, nameof(calculationsRepository));
            Guard.ArgumentNotNull(logger, nameof(logger));
            Guard.ArgumentNotNull(searchRepository, nameof(searchRepository));
            Guard.ArgumentNotNull(calculationValidator, nameof(calculationValidator));
            Guard.ArgumentNotNull(buildProjectsService, nameof(buildProjectsService));
            Guard.ArgumentNotNull(specificationRepository, nameof(specificationRepository));
            Guard.ArgumentNotNull(policiesApiClient, nameof(policiesApiClient));
            Guard.ArgumentNotNull(cacheProvider, nameof(cacheProvider));
            Guard.ArgumentNotNull(resiliencePolicies, nameof(resiliencePolicies));
            Guard.ArgumentNotNull(calculationVersionRepository, nameof(calculationVersionRepository));
            Guard.ArgumentNotNull(jobsApiClient, nameof(jobsApiClient));
            Guard.ArgumentNotNull(sourceCodeService, nameof(sourceCodeService));
            Guard.ArgumentNotNull(featureToggle, nameof(featureToggle));
            Guard.ArgumentNotNull(buildProjectsRepository, nameof(buildProjectsRepository));
            Guard.ArgumentNotNull(calculationCodeReferenceUpdate, nameof(calculationCodeReferenceUpdate));
            Guard.ArgumentNotNull(calculationCreateModelValidator, nameof(calculationCreateModelValidator));
            Guard.ArgumentNotNull(calculationEditModelValidator, nameof(calculationEditModelValidator));

            _calculationsRepository = calculationsRepository;
            _logger = logger;
            _searchRepository = searchRepository;
            _calculationValidator = calculationValidator;
            _specsRepository = specificationRepository;
            _cacheProvider = cacheProvider;
            _calculationRepositoryPolicy = resiliencePolicies.CalculationsRepository;
            _calculationVersionRepository = calculationVersionRepository;
            _calculationSearchRepositoryPolicy = resiliencePolicies.CalculationsSearchRepository;
            _cachePolicy = resiliencePolicies.CacheProviderPolicy;
            _specificationsRepositoryPolicy = resiliencePolicies.SpecificationsRepositoryPolicy;
            _calculationVersionsRepositoryPolicy = resiliencePolicies.CalculationsVersionsRepositoryPolicy;
            _jobsApiClient = jobsApiClient;
            _jobsApiClientPolicy = resiliencePolicies.JobsApiClient;
            _sourceCodeService = sourceCodeService;
            _featureToggle = featureToggle;
            _buildProjectsService = buildProjectsService;
            _buildProjectsRepository = buildProjectsRepository;
            _buildProjectRepositoryPolicy = resiliencePolicies.BuildProjectRepositoryPolicy;
            _calculationCodeReferenceUpdate = calculationCodeReferenceUpdate;
            _mapper = mapper;
            _calculationCodeReferenceUpdate = calculationCodeReferenceUpdate;
            _calculationCreateModelValidator = calculationCreateModelValidator;
            _calculationEditModelValidator = calculationEditModelValidator;
        }

        public async Task<ServiceHealth> IsHealthOk()
        {
            ServiceHealth calcsRepoHealth = await ((IHealthChecker)_calculationsRepository).IsHealthOk();
            (bool Ok, string Message) searchRepoHealth = await _searchRepository.IsHealthOk();

            ServiceHealth health = new ServiceHealth()
            {
                Name = nameof(CalculationService)
            };
            health.Dependencies.AddRange(calcsRepoHealth.Dependencies);
            health.Dependencies.Add(new DependencyHealth { HealthOk = searchRepoHealth.Ok, DependencyName = _searchRepository.GetType().GetFriendlyName(), Message = searchRepoHealth.Message });

            return health;
        }

        public async Task<IActionResult> GetCalculationHistory(HttpRequest request)
        {
            request.Query.TryGetValue("calculationId", out Microsoft.Extensions.Primitives.StringValues calcId);

            string calculationId = calcId.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(calculationId))
            {
                _logger.Error("No calculation Id was provided to GetCalculationHistory");

                return new BadRequestObjectResult("Null or empty calculation Id provided");
            }

            IEnumerable<CalculationVersion> history = await _calculationVersionsRepositoryPolicy.ExecuteAsync(() => _calculationVersionRepository.GetVersions(calculationId));

            if (history.IsNullOrEmpty())
            {
                _logger.Information($"A calculation was not found for calculation id {calculationId}");

                return new NotFoundResult();
            }

            return new OkObjectResult(history);
        }

        public async Task<IActionResult> GetCalculationVersions(HttpRequest request)
        {
            string json = await request.GetRawBodyStringAsync();

            CalculationVersionsCompareModel compareModel = JsonConvert.DeserializeObject<CalculationVersionsCompareModel>(json);

            //Need custom validator here

            if (compareModel == null || string.IsNullOrEmpty(compareModel.CalculationId) || compareModel.Versions == null || compareModel.Versions.Count() < 2)
            {
                _logger.Warning("A null or invalid compare model was provided for comparing models");

                return new BadRequestObjectResult("A null or invalid compare model was provided for comparing models");
            }

            IEnumerable<CalculationVersion> allVersions = await _calculationVersionsRepositoryPolicy.ExecuteAsync(() => _calculationVersionRepository.GetVersions(compareModel.CalculationId));

            if (allVersions.IsNullOrEmpty())
            {
                _logger.Information($"No history was not found for calculation id {compareModel.CalculationId}");

                return new NotFoundResult();
            }

            IList<CalculationVersion> versions = new List<CalculationVersion>();

            foreach (int version in compareModel.Versions)
            {
                versions.Add(allVersions.FirstOrDefault(m => m.Version == version));
            }

            if (!versions.Any())
            {
                _logger.Information($"A calculation was not found for calculation id {compareModel.CalculationId}");

                return new NotFoundResult();
            }

            return new OkObjectResult(versions);
        }

        public async Task<IActionResult> GetCalculationCurrentVersion(HttpRequest request)
        {
            request.Query.TryGetValue("calculationId", out Microsoft.Extensions.Primitives.StringValues calcId);

            string calculationId = calcId.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(calculationId))
            {
                _logger.Error("No calculation Id was provided to GetCalculationCurrentVersion");

                return new BadRequestObjectResult("Null or empty calculation Id provided");
            }

            string cacheKey = $"{CacheKeys.CurrentCalculation}{calculationId}";

            CalculationCurrentVersion calculation = await _cachePolicy.ExecuteAsync(() => _cacheProvider.GetAsync<CalculationCurrentVersion>(cacheKey));

            if (calculation == null)
            {
                Calculation repoCalculation = await _calculationsRepository.GetCalculationById(calculationId);
                if (repoCalculation == null)
                {
                    _logger.Information($"A calculation was not found for calculation id {calculationId}");

                    return new NotFoundResult();
                }

                if (repoCalculation.Current == null)
                {
                    _logger.Information($"A current calculation was not found for calculation id {calculationId}");

                    return new NotFoundResult();
                }

                calculation = GetCurrentVersionFromCalculation(repoCalculation);

                await _cachePolicy.ExecuteAsync(() => _cacheProvider.SetAsync(cacheKey, calculation, TimeSpan.FromDays(7), true));
            }

            return new OkObjectResult(calculation);
        }

        async public Task<IActionResult> GetCalculationById(HttpRequest request)
        {
            request.Query.TryGetValue("calculationId", out Microsoft.Extensions.Primitives.StringValues calcId);

            string calculationId = calcId.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(calculationId))
            {
                _logger.Error("No calculation Id was provided to GetCalculationById");

                return new BadRequestObjectResult("Null or empty calculation Id provided");
            }

            Models.Calcs.Calculation calculation = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationById(calculationId));

            if (calculation != null)
            {
                _logger.Information($"A calculation was found for calculation id {calculationId}");

                return new OkObjectResult(calculation);
            }

            _logger.Information($"A calculation was not found for calculation id {calculationId}");

            return new NotFoundResult();
        }

        public async Task<IActionResult> GetCurrentCalculationsForSpecification(HttpRequest request)
        {
            request.Query.TryGetValue("specificationId", out Microsoft.Extensions.Primitives.StringValues specId);

            string specificationId = specId.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(specificationId))
            {
                _logger.Warning("No specificationId was provided to GetCalculationsForSpecification");

                return new BadRequestObjectResult("Null or empty specificationId provided");
            }
            string cacheKey = $"{CacheKeys.CurrentCalculationsForSpecification}{specificationId}";

            List<CalculationCurrentVersion> calculations = await _cachePolicy.ExecuteAsync(() => _cacheProvider.GetAsync<List<CalculationCurrentVersion>>(cacheKey));
            if (calculations == null)
            {
                IEnumerable<Calculation> calculationsFromRepository = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId));

                if (calculationsFromRepository == null)
                {
                    _logger.Warning($"Calculations from repository returned null for specification ID of '{specificationId}'");

                    return new InternalServerErrorResult("Calculations from repository returned null");
                }

                calculations = new List<CalculationCurrentVersion>(calculationsFromRepository.Count());
                foreach (Calculation calculation in calculationsFromRepository)
                {
                    calculations.Add(GetCurrentVersionFromCalculation(calculation));
                }

                await _cachePolicy.ExecuteAsync(() => _cacheProvider.SetAsync(cacheKey, calculations, TimeSpan.FromDays(7), true));
            }

            return new OkObjectResult(calculations);
        }

        public async Task<IActionResult> GetCalculationSummariesForSpecification(HttpRequest request)
        {
            request.Query.TryGetValue("specificationId", out Microsoft.Extensions.Primitives.StringValues specId);

            string specificationId = specId.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(specificationId))
            {
                _logger.Warning("No specificationId was provided to GetCalculationSummariesForSpecification");

                return new BadRequestObjectResult("Null or empty specificationId provided");
            }
            string cacheKey = $"{CacheKeys.CalculationsSummariesForSpecification}{specificationId}";

            List<CalculationSummaryModel> calculations = await _cachePolicy.ExecuteAsync(() => _cacheProvider.GetAsync<List<CalculationSummaryModel>>(cacheKey));
            if (calculations == null)
            {
                IEnumerable<Calculation> calculationsFromRepository = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId));

                if (calculationsFromRepository == null)
                {
                    _logger.Warning($"Calculations from repository returned null for specification ID of '{specificationId}'");

                    return new InternalServerErrorResult("Calculations from repository returned null");
                }

                calculations = new List<CalculationSummaryModel>(calculationsFromRepository.Count());
                foreach (Calculation calculation in calculationsFromRepository)
                {
                    calculations.Add(GetCalculationSummaryFromCalculation(calculation));
                }

                await _cachePolicy.ExecuteAsync(() => _cacheProvider.SetAsync(cacheKey, calculations, TimeSpan.FromDays(7), true));
            }

            return new OkObjectResult(calculations);
        }

        public async Task<IActionResult> CreateAdditionalCalculation(string specificationId, CalculationCreateModel model, Reference author, string correlationId)
        {
            Guard.ArgumentNotNull(model, nameof(model));
            Guard.ArgumentNotNull(author, nameof(author));

            if (string.IsNullOrWhiteSpace(model.Id))
            {
                model.Id = Guid.NewGuid().ToString();
            }

            model.SpecificationId = specificationId;

            BadRequestObjectResult validationResult = (await _calculationCreateModelValidator.ValidateAsync(model)).PopulateModelState();

            if (validationResult != null)
            {
                return validationResult;
            }

            Calculation calculation = new Calculation
            {
                Id = model.Id,
                FundingStreamId = model.FundingStreamId,
                SpecificationId = model.SpecificationId,
            };

            CalculationVersion calculationVersion = new CalculationVersion
            {
                CalculationId = calculation.Id,
                PublishStatus = PublishStatus.Draft,
                Author = author,
                Date = DateTimeOffset.Now.ToLocalTime(),
                Version = 1,
                SourceCode = model.SourceCode,
                Description = model.Description,
                ValueType = model.ValueType.Value,
                CalculationType = CalculationType.Additional,
                WasTemplateCalculation = false,
                Namespace = CalculationNamespace.Additional,
                Name = model.Name
            };

            calculation.Current = calculationVersion;

            IActionResult nameValidResult = await IsCalculationNameValid(calculation.Id, calculation.Name, null);

            if (nameValidResult is ConflictResult)
            {
                _logger.Error("Calculation with the same generated source code name already exists in this specification. Calculation Name {calcName} and Specification {specificationId}", calculation.Name, calculation.SpecificationId);
                return new BadRequestObjectResult($"Calculation with the same generated source code name already exists in this specification. Calculation Name {calculation.Name} and Specification {calculation.SpecificationId}");
            }

            calculation.Current.SourceCodeName = VisualBasicTypeGenerator.GenerateIdentifier(calculation.Name);

            HttpStatusCode result = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.CreateDraftCalculation(calculation));

            if (result.IsSuccess())
            {
                await _calculationVersionsRepositoryPolicy.ExecuteAsync(() => _calculationVersionRepository.SaveVersion(calculationVersion));

                await UpdateSearch(calculation);

                Job job = null;

                try
                {
                    job = await SendInstructAllocationsToJobService(calculation.SpecificationId, author.Id, author.Name, new Trigger
                    {
                        EntityId = calculation.Id,
                        EntityType = nameof(Calculation),
                        Message = $"Saving calculation: '{calculation.Id}' for specification: '{calculation.SpecificationId}'"
                    }, correlationId);

                    if (job != null)
                    {
                        _logger.Information($"New job of type '{JobConstants.DefinitionNames.CreateInstructAllocationJob}' created with id: '{job.Id}'");

                        return new OkObjectResult(calculation);
                    }
                    else
                    {
                        string errorMessage = $"Failed to create job of type '{JobConstants.DefinitionNames.CreateInstructAllocationJob}' on specification '{calculation.SpecificationId}'";

                        _logger.Error(errorMessage);

                        return new InternalServerErrorResult(errorMessage);
                    }
                }
                catch (Exception ex)
                {
                    return new InternalServerErrorResult(ex.Message);
                }
            }
            else
            {
                string errorMessage = $"There was problem creating a new calculation with name {calculation.Name} in Cosmos Db with status code {(int)result}";

                _logger.Error(errorMessage);

                return new InternalServerErrorResult(errorMessage);
            }
        }

        public async Task UpdateCalculationsForSpecification(Message message)
        {
            SpecificationVersionComparisonModel specificationVersionComparison = message.GetPayloadAsInstanceOf<SpecificationVersionComparisonModel>();

            if (specificationVersionComparison == null || specificationVersionComparison.Current == null)
            {
                _logger.Error("A null specificationVersionComparison was provided to UpdateCalculationsForSpecification");

                throw new InvalidModelException(nameof(SpecificationVersionComparisonModel), new[] { "Null or invalid model provided" });
            }

            if (specificationVersionComparison.HasNoChanges && !specificationVersionComparison.HasNameChange)
            {
                _logger.Information("No changes detected");
                return;
            }

            string specificationId = specificationVersionComparison.Id;

            IEnumerable<Calculation> calculations = (await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId))).ToArraySafe();

            if (calculations.IsNullOrEmpty())
            {
                _logger.Information($"No calculations found for specification id: {specificationId}");
                return;
            }

            IEnumerable<string> fundingStreamIds = specificationVersionComparison.Current.FundingStreams?.Select(m => m.Id);

            IList<CalculationIndex> calcIndexes = new List<CalculationIndex>();

            foreach (Calculation calculation in calculations)
            {
                calcIndexes.Add(CreateCalculationIndexItem(calculation));
            }

            BuildProject buildProject = await _buildProjectsService.GetBuildProjectForSpecificationId(specificationId);

            await TaskHelper.WhenAllAndThrow(
                _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.UpdateCalculations(calculations)),
                _calculationSearchRepositoryPolicy.ExecuteAsync(() => _searchRepository.Index(calcIndexes))
            );

            IDictionary<string, string> properties = message.BuildMessageProperties();

            string userId = properties.ContainsKey("user-id") ? properties["user-id"] : string.Empty;
            string userName = properties.ContainsKey("user-name") ? properties["user-name"] : string.Empty;
            string correlationId = message.GetCorrelationId();

            Trigger trigger = new Trigger
            {
                EntityId = specificationId,
                EntityType = nameof(Specification),
                Message = $"Updating calculations for specification: '{specificationId}'"
            };

            Job job = await SendInstructAllocationsToJobService(specificationId, userId, userName, trigger, correlationId);

            if (job == null)
            {
                string errorMessage = $"Failed to create job: '{JobConstants.DefinitionNames.CreateInstructAllocationJob} for specification id '{specificationId}'";

                _logger.Error(errorMessage);

                throw new RetriableException(errorMessage);
            }

            _logger.Information($"New job of type '{job.JobDefinitionId}' created with id: '{job.Id}'");
        }

        public async Task<IEnumerable<Calculation>> UpdateCalculationCodeOnCalculationChange(CalculationVersionComparisonModel comparison, Reference user)
        {
            string oldCalcSourceCodeName = VisualBasicTypeGenerator.GenerateIdentifier(comparison.Previous.Name);
            string newCalcSourceCodeName = VisualBasicTypeGenerator.GenerateIdentifier(comparison.Current.Name);

            return await UpdateCalculationCodeOnCalculationChange(oldCalcSourceCodeName, newCalcSourceCodeName, comparison.SpecificationId, user);
        }

        private async Task<IEnumerable<Calculation>> UpdateCalculationCodeOnCalculationChange(string oldCalcSourceCodeName, string newCalcSourceCodeName, string specificationId, Reference user)
        {
            List<Calculation> updatedCalculations = new List<Calculation>();

            if (oldCalcSourceCodeName != newCalcSourceCodeName)
            {
                IEnumerable<Calculation> calculations = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId));

                foreach (Calculation calculation in calculations)
                {
                    string sourceCode = calculation.Current.SourceCode;
                    string result = _calculationCodeReferenceUpdate.ReplaceSourceCodeReferences(sourceCode,
                        oldCalcSourceCodeName,
                        newCalcSourceCodeName);

                    if (result != sourceCode)
                    {
                        CalculationVersion calculationVersion = calculation.Current.Clone() as CalculationVersion;
                        calculationVersion.SourceCode = result;

                        UpdateCalculationResult updateCalculationResult = await UpdateCalculation(calculation, calculationVersion, user, updateBuildProject: false);

                        updatedCalculations.Add(updateCalculationResult.Calculation);
                    }
                }
            }

            return updatedCalculations;
        }

        public async Task<IActionResult> EditCalculation(string specificationId, string calculationId, CalculationEditModel calculationEditModel, Reference author, string correlationId)
        {
            Guard.ArgumentNotNull(calculationEditModel, nameof(calculationEditModel));
            Guard.ArgumentNotNull(author, nameof(author));

            calculationEditModel.SpecificationId = specificationId;
            calculationEditModel.CalculationId = calculationId;

            BadRequestObjectResult validationResult = (await _calculationEditModelValidator.ValidateAsync(calculationEditModel)).PopulateModelState();

            if (validationResult != null)
            {
                return validationResult;
            }

            Calculation calculation = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationById(calculationId));

            if (calculation == null)
            {
                _logger.Error($"A calculation was not found for calculation id {calculationId}");

                return new NotFoundResult();
            }

            CalculationVersion calculationVersion = calculation.Current.Clone() as CalculationVersion;

            calculationVersion.SourceCode = calculationEditModel.SourceCode;
            calculationVersion.Name = calculationEditModel.Name;
            calculationVersion.ValueType = calculationEditModel.ValueType.Value;
            calculationVersion.SourceCodeName = VisualBasicTypeGenerator.GenerateIdentifier(calculationEditModel.Name);
            calculationVersion.Description = calculationEditModel.Description;

            UpdateCalculationResult result = await UpdateCalculation(calculation, calculationVersion, author);

            Job job = null;

            try
            {
                job = await SendInstructAllocationsToJobService(result.BuildProject.SpecificationId, author.Id, author.Name, new Trigger
                {
                    EntityId = calculation.Id,
                    EntityType = nameof(Calculation),
                    Message = $"Saving calculation: '{calculationId}' for specification: '{calculation.SpecificationId}'"
                }, correlationId);

                if (job != null)
                {
                    _logger.Information($"New job of type '{JobConstants.DefinitionNames.CreateInstructAllocationJob}' created with id: '{job.Id}'");

                    return new OkObjectResult(result.CurrentVersion);
                }
                else
                {
                    string errorMessage = $"Failed to create job of type '{JobConstants.DefinitionNames.CreateInstructAllocationJob}' on specification '{calculation.SpecificationId}'";

                    _logger.Error(errorMessage);

                    return new InternalServerErrorResult(errorMessage);
                }
            }
            catch (Exception ex)
            {
                return new InternalServerErrorResult(ex.Message);
            }
        }


        private async Task<UpdateCalculationResult> UpdateCalculation(Calculation calculation, CalculationVersion calculationVersion, Reference user, bool updateBuildProject = true)
        {
            Guard.ArgumentNotNull(calculation, nameof(calculation));
            Guard.ArgumentNotNull(calculationVersion, nameof(calculationVersion));
            Guard.ArgumentNotNull(user, nameof(user));

            if (calculation.Current == null)
            {
                _logger.Warning($"Current for {calculation.Id} was null and needed recreating.");
                calculation.Current = calculationVersion;
            }

            CalculationVersion previousCalculationVersion = calculation.Current;

            calculationVersion.Author = user;

            HttpStatusCode statusCode = await UpdateCalculation(calculation, calculationVersion, previousCalculationVersion);

            if (statusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"Update calculation returned status code '{statusCode}' instead of OK");
            }

            BuildProject buildProject = null;

            if (updateBuildProject)
            {
                buildProject = await UpdateBuildProject(calculation.SpecificationId);
            }

            SpecificationSummary specificationSummary = await _specsRepository.GetSpecificationSummaryById(calculation.SpecificationId);

            await UpdateSearch(calculation);

            CalculationCurrentVersion currentVersion = GetCurrentVersionFromCalculation(calculation);
            await UpdateCalculationInCache(calculation, currentVersion);

            return new UpdateCalculationResult()
            {
                BuildProject = buildProject,
                Calculation = calculation,
                CurrentVersion = currentVersion,
            };
        }

        public async Task<IActionResult> UpdateCalculationStatus(HttpRequest request)
        {
            request.Query.TryGetValue("calculationId", out Microsoft.Extensions.Primitives.StringValues calcId);

            string calculationId = calcId.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(calculationId))
            {
                _logger.Error("No calculation Id was provided to EditCalculationStatus");

                return new BadRequestObjectResult("Null or empty calculation Id provided");
            }

            string json = await request.GetRawBodyStringAsync();

            EditStatusModel editStatusModel = null;

            try
            {
                editStatusModel = JsonConvert.DeserializeObject<EditStatusModel>(json);

                if (editStatusModel == null)
                {
                    _logger.Error("A null status model was provided");
                    return new BadRequestObjectResult("Null status model provided");
                }
            }
            catch (JsonSerializationException jse)
            {
                _logger.Error(jse, $"An invalid status was provided for calculation: {calculationId}");

                return new BadRequestObjectResult("An invalid status was provided");
            }

            Calculation calculation = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationById(calculationId));

            if (calculation == null)
            {
                _logger.Warning($"A calculation was not found for calculation id {calculationId}");

                return new NotFoundObjectResult("Calculation not found");
            }

            if (calculation.Current == null)
            {
                _logger.Warning($"A current calculation was not found for calculation id {calculationId}");

                return new NotFoundResult();
            }

            if (calculation.Current.PublishStatus == editStatusModel.PublishStatus)
            {
                return new OkObjectResult(calculation.Current);
            }

            if ((calculation.Current.PublishStatus == Models.Versioning.PublishStatus.Approved || calculation.Current.PublishStatus == Models.Versioning.PublishStatus.Updated) && editStatusModel.PublishStatus == Models.Versioning.PublishStatus.Draft)
            {
                return new BadRequestObjectResult("Publish status can't be changed to Draft from Updated or Approved");
            }

            SpecificationSummary specificationSummary = await _specificationsRepositoryPolicy.ExecuteAsync(() => _specsRepository.GetSpecificationSummaryById(calculation.SpecificationId));
            if (specificationSummary == null)
            {
                return new PreconditionFailedResult("Specification not found");
            }

            Reference user = request.GetUser();

            CalculationVersion previousCalculationVersion = calculation.Current;

            CalculationVersion calculationVersion = calculation.Current.Clone() as CalculationVersion;

            calculationVersion.PublishStatus = editStatusModel.PublishStatus;

            HttpStatusCode statusCode = await UpdateCalculation(calculation, calculationVersion, previousCalculationVersion);

            if (!statusCode.IsSuccess())
            {
                return new StatusCodeResult((int)statusCode);
            }

            await UpdateBuildProject(calculation.SpecificationId);

            await UpdateSearch(calculation);

            PublishStatusResultModel result = new PublishStatusResultModel()
            {
                PublishStatus = calculation.Current.PublishStatus,
            };

            CalculationCurrentVersion currentVersion = GetCurrentVersionFromCalculation(calculation);

            await UpdateCalculationInCache(calculation, currentVersion);

            return new OkObjectResult(result);
        }

        public async Task<IActionResult> GetCalculationCodeContext(HttpRequest request)
        {
            request.Query.TryGetValue("specificationId", out Microsoft.Extensions.Primitives.StringValues specId);

            string specificationId = specId.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(specificationId))
            {
                _logger.Error("No specificationId was provided to GetCalculationCodeContext");

                return new BadRequestObjectResult("Null or empty specificationId provided");
            }

            _logger.Information("Generating code context for {specificationId}", specificationId);

            BuildProject buildProject = await _buildProjectsService.GetBuildProjectForSpecificationId(specificationId);

            IEnumerable<Calculation> calculations = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId));

            buildProject.Build = _sourceCodeService.Compile(buildProject, calculations ?? Enumerable.Empty<Calculation>());

            if (buildProject.Build == null)
            {
                _logger.Error($"Build was null for Specification {specificationId} with Build Project ID {buildProject.Id}");

                return new StatusCodeResult(500);
            }

            IEnumerable<TypeInformation> result = await _sourceCodeService.GetTypeInformation(buildProject);

            return new OkObjectResult(result);
        }

        public async Task<IActionResult> ReIndex()
        {
            //Not spending too much time her as probably will go to sql server
            await _searchRepository.DeleteIndex();

            IEnumerable<Calculation> calculations = await _calculationsRepository.GetAllCalculations();

            IList<CalculationIndex> calcIndexItems = new List<CalculationIndex>();

            Dictionary<string, SpecificationSummary> specifications = new Dictionary<string, SpecificationSummary>();

            foreach (Calculation calculation in calculations)
            {
                SpecificationSummary specification = null;
                if (specifications.ContainsKey(calculation.SpecificationId))
                {
                    specification = specifications[calculation.SpecificationId];
                }
                else
                {
                    specification = await _specificationsRepositoryPolicy.ExecuteAsync(() => _specsRepository.GetSpecificationSummaryById(calculation.SpecificationId));
                    if (specification != null)
                    {
                        specifications.Add(calculation.SpecificationId, specification);
                    }
                }

                CalculationIndex indexItem = CreateCalculationIndexItem(calculation);
                //indexItem.CalculationType = calculation.AllocationLine == null ? CalculationType.Number.ToString() : CalculationType.Funding.ToString();

                calcIndexItems.Add(indexItem);
            }

            IEnumerable<IndexError> indexingResults = await _calculationSearchRepositoryPolicy.ExecuteAsync(() => _searchRepository.Index(calcIndexItems));

            if (indexingResults.Any())
            {
                _logger.Error($"Failed to re-index calculation with the following errors: {string.Join(";", indexingResults.Select(m => m.ErrorMessage).ToArraySafe())}");

                return new StatusCodeResult(500);
            }

            return new NoContentResult();
        }

        public async Task<IActionResult> GetCalculationStatusCounts(HttpRequest request)
        {
            string json = await request.GetRawBodyStringAsync();

            SpecificationListModel specifications = JsonConvert.DeserializeObject<SpecificationListModel>(json);

            if (specifications == null)
            {
                _logger.Error("Null specification model provided");

                return new BadRequestObjectResult("Null specifications model provided");
            }

            if (specifications.SpecificationIds.IsNullOrEmpty())
            {
                _logger.Error("Null or empty specification ids provided");

                return new BadRequestObjectResult("Null or empty specification ids provided");
            }

            ConcurrentBag<CalculationStatusCountsModel> statusCountModels = new ConcurrentBag<CalculationStatusCountsModel>();

            IList<Task> statusCountsTasks = new List<Task>();

            foreach (string specificationId in specifications.SpecificationIds)
            {
                statusCountsTasks.Add(Task.Run(async () =>
                {
                    StatusCounts statusCounts = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetStatusCounts(specificationId));

                    statusCountModels.Add(new CalculationStatusCountsModel
                    {
                        SpecificationId = specificationId,
                        Approved = statusCounts.Approved,
                        Updated = statusCounts.Updated,
                        Draft = statusCounts.Draft
                    });

                }));
            }

            try
            {
                await TaskHelper.WhenAllAndThrow(statusCountsTasks.ToArray());
            }
            catch (Exception ex)
            {
                return new InternalServerErrorResult($"An error occurred when obtaining calculation steps with the follwing message: \n {ex.Message}");
            }

            return new OkObjectResult(statusCountModels);
        }

        public async Task<IActionResult> GetCalculationByName(CalculationGetModel model)
        {
            Guard.ArgumentNotNull(model, nameof(model));

            if (string.IsNullOrWhiteSpace(model.SpecificationId))
            {
                _logger.Error("No specification id was provided to GetCalculationByName");
                return new BadRequestObjectResult("Null or empty specification id provided");
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                _logger.Error("No calculation name was provided to GetCalculationByName");
                return new BadRequestObjectResult("Null or empty calculation name provided");
            }

            Calculation calculation = await _calculationsRepository.GetCalculationsBySpecificationIdAndCalculationName(model.SpecificationId, model.Name);

            if (calculation == null)
            {
                _logger.Information($"A calculation was not found for specification id {model.SpecificationId} and name {model.Name}");

                return new NotFoundResult();
            }

            return new OkObjectResult(calculation.Current);
        }

        private async Task UpdateSearch(Calculation calculation)
        {
            IEnumerable<IndexError> indexingResults = await _searchRepository.Index(new List<CalculationIndex>
            {
                CreateCalculationIndexItem(calculation)
            });
        }

        private CalculationIndex CreateCalculationIndexItem(Calculation calculation)
        {
            return new CalculationIndex
            {
                Id = calculation.Id,
                SpecificationId = calculation.SpecificationId,
                Name = calculation.Current.Name,
                ValueType = calculation.Current.ValueType.ToString(),
                FundingStreamId = calculation.FundingStreamId,
                Namespace = calculation.Current.Namespace.ToString(),
                CalculationType = calculation.Current.CalculationType.ToString(),
                Description = calculation.Current.Description,
                WasTemplateCalculation = calculation.Current.WasTemplateCalculation
            };
        }

        public async Task<IActionResult> IsCalculationNameValid(string specificationId, string calculationName, string existingCalculationId)
        {
            Guard.IsNullOrWhiteSpace(specificationId, nameof(specificationId));
            Guard.IsNullOrWhiteSpace(calculationName, nameof(calculationName));

            SpecificationSummary specification = await _specificationsRepositoryPolicy.ExecuteAsync(() => _specsRepository.GetSpecificationSummaryById(specificationId));

            if (specification == null)
            {
                return new NotFoundResult();
            }

            IEnumerable<Calculation> existingCalculations = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId));

            if (!existingCalculations.IsNullOrEmpty())
            {
                string calcSourceName = VisualBasicTypeGenerator.GenerateIdentifier(calculationName);

                foreach (Calculation calculation in existingCalculations)
                {
                    if (calculation.Id != existingCalculationId && string.Compare(calculation.Current.SourceCodeName, calcSourceName, true) == 0)
                    {
                        return new ConflictResult();
                    }
                }
            }

            return new OkResult();
        }

        public async Task<IActionResult> DuplicateCalcNamesMigration()
        {
            try
            {
                _logger.Information("Starting migration for duplicate calc names");

                Task<IEnumerable<Calculation>> getAllCalcsTask = _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetAllCalculations());
                Task<IEnumerable<SpecificationSummary>> getAllSpecsTask = _specsRepository.GetAllSpecificationSummaries();

                await TaskHelper.WhenAllAndThrow(getAllCalcsTask, getAllSpecsTask);

                IEnumerable<Calculation> allCalcs = getAllCalcsTask.Result;
                IEnumerable<SpecificationSummary> allSpecs = getAllSpecsTask.Result;

                _logger.Information("Processing calcs for duplicate calc names migration");

                foreach (Calculation calculation in allCalcs)
                {
                    calculation.Current.SourceCodeName = VisualBasicTypeGenerator.GenerateIdentifier(calculation.Name);
                    await _calculationsRepository.UpdateCalculation(calculation);

                    SpecificationSummary specificationSummary = allSpecs.SingleOrDefault(s => s.Id == calculation.SpecificationId);

                    if (specificationSummary != null)
                    {
                        await UpdateSearch(calculation);

                        CalculationCurrentVersion currentVersion = GetCurrentVersionFromCalculation(calculation);
                        await UpdateCalculationInCache(calculation, currentVersion);
                    }
                    else
                    {
                        _logger.Warning($"Could not find specification with id '{calculation.SpecificationId} for calculation '{calculation.Id} when performing migration for duplicate calc names.");
                    }
                }

                return new OkResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to perform migration for duplicate calc names. {ex.Message}");
                return new InternalServerErrorResult(ex.Message);
            }
        }

        public async Task ResetCalculationForFieldDefinitionChanges(IEnumerable<DatasetSpecificationRelationshipViewModel> relationships, string specificationId, IEnumerable<string> currentFieldDefinitionNames)
        {
            Guard.ArgumentNotNull(relationships, nameof(relationships));
            Guard.IsNullOrWhiteSpace(specificationId, nameof(specificationId));
            Guard.ArgumentNotNull(currentFieldDefinitionNames, nameof(currentFieldDefinitionNames));

            IEnumerable<Calculation> calculations = (await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId))).ToList();

            if (calculations.IsNullOrEmpty())
            {
                _logger.Information($"No calculations found to reset for specification id '{specificationId}'");
                return;
            }

            List<string> fieldIdentifiers = new List<string>();

            foreach (DatasetSpecificationRelationshipViewModel datasetSpecificationRelationshipViewModel in relationships)
            {
                fieldIdentifiers.AddRange(currentFieldDefinitionNames.Select(m => $"Datasets.{VisualBasicTypeGenerator.GenerateIdentifier(datasetSpecificationRelationshipViewModel.Name)}.{VisualBasicTypeGenerator.GenerateIdentifier(m)}"));
            }

            await _cacheProvider.RemoveAsync<List<DatasetSchemaRelationshipModel>>($"{CacheKeys.DatasetRelationshipFieldsForSpecification}{specificationId}");

            IEnumerable<Calculation> calcsToUpdate = calculations.Where(m => SourceCodeHelpers.CodeContainsFullyQualifiedDatasetFieldIdentifier(m.Current?.SourceCode, fieldIdentifiers));

            if (calcsToUpdate.IsNullOrEmpty())
            {
                _logger.Information($"No calculations required resetting for specification id '{specificationId}'");
                return;
            }

            foreach (Calculation calculation in calcsToUpdate)
            {
                string sourceCode = calculation.Current.SourceCode;

                CalculationVersion calculationVersion = calculation.Current.Clone() as CalculationVersion;

                calculationVersion.SourceCode = SourceCodeHelpers.CommentOutCode(sourceCode, reasonForCommenting, exceptionMessage, exceptionType);
                calculationVersion.Comment = reasonForCommenting;
                await UpdateCalculation(calculation, calculationVersion, new Reference("System", "System"));
            }

            await _sourceCodeService.DeleteAssembly(specificationId);
        }

        private async Task<BuildProject> UpdateBuildProject(string specificationId)
        {
            Task<IEnumerable<Calculation>> calculationsRequest = _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId));
            Task<BuildProject> buildProjectRequest = _buildProjectsService.GetBuildProjectForSpecificationId(specificationId);
           
            await TaskHelper.WhenAllAndThrow(calculationsRequest, buildProjectRequest);

            List<Calculation> calculations = new List<Calculation>(calculationsRequest.Result);
            BuildProject buildProject = buildProjectRequest.Result;
           
            return await UpdateBuildProject(specificationId, calculations, buildProject);
        }

        private async Task<BuildProject> UpdateBuildProject(string specificationId, IEnumerable<Calculation> calculations, BuildProject buildProject = null)
        {
            if (buildProject == null)
            {
                buildProject = await _buildProjectsService.GetBuildProjectForSpecificationId(specificationId);
            }

            CompilerOptions compilerOptions = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCompilerOptions(specificationId));

            if (compilerOptions == null)
            {
                compilerOptions = new CompilerOptions();
            }

            //forcing off for calc runs only
            compilerOptions.OptionStrictEnabled = false;

            buildProject.Build = _sourceCodeService.Compile(buildProject, calculations, compilerOptions);

            await _sourceCodeService.SaveSourceFiles(buildProject.Build.SourceFiles, specificationId, SourceCodeType.Release);

            await _sourceCodeService.SaveAssembly(buildProject);

            if (!_featureToggle.IsDynamicBuildProjectEnabled())
            {
                await _buildProjectRepositoryPolicy.ExecuteAsync(() => _buildProjectsRepository.UpdateBuildProject(buildProject));
            }

            return buildProject;
        }

        private CalculationCurrentVersion GetCurrentVersionFromCalculation(Calculation calculation)
        {
            CalculationCurrentVersion calculationCurrentVersion = new CalculationCurrentVersion
            {
                SpecificationId = calculation.SpecificationId,
                Author = calculation.Current?.Author,
                Date = calculation.Current?.Date,
                FundingStreamId = calculation.FundingStreamId,
                PublishStatus = calculation.Current.PublishStatus,
                Id = calculation.Id,
                Name = calculation.Name,
                SourceCode = calculation.Current?.SourceCode ?? CodeGenerationConstants.VisualBasicDefaultSourceCode,
                Version = calculation.Current.Version,
                CalculationType = calculation.Current.CalculationType.ToString(),
                SourceCodeName = calculation.Current.SourceCodeName
            };

            return calculationCurrentVersion;
        }

        private CalculationSummaryModel GetCalculationSummaryFromCalculation(Calculation calculation)
        {
            CalculationSummaryModel calculationCurrentVersion = new CalculationSummaryModel
            {
                Id = calculation.Id,
                Name = calculation.Name,
                CalculationType = calculation.Current.CalculationType,
                Status = calculation.Current.PublishStatus,
                Version = calculation.Current.Version
            };

            return calculationCurrentVersion;
        }

        private async Task<Job> SendInstructAllocationsToJobService(string specificationId, string userId, string userName, Trigger trigger, string correlationId)
        {
            IEnumerable<Calculation> allCalculations = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.GetCalculationsBySpecificationId(specificationId));

            bool generateCalculationAggregations = allCalculations.IsNullOrEmpty() ? false :
                SourceCodeHelpers.HasCalculationAggregateFunctionParameters(allCalculations.Select(m => m.Current.SourceCode));

            JobCreateModel job = new JobCreateModel
            {
                InvokerUserDisplayName = userName,
                InvokerUserId = userId,
                JobDefinitionId = generateCalculationAggregations ?
                    JobConstants.DefinitionNames.CreateInstructGenerateAggregationsAllocationJob :
                    JobConstants.DefinitionNames.CreateInstructAllocationJob,
                SpecificationId = specificationId,
                Properties = new Dictionary<string, string>
                    {
                        { "specification-id", specificationId }
                    },
                Trigger = trigger,
                CorrelationId = correlationId
            };

            try
            {
                return await _jobsApiClientPolicy.ExecuteAsync(() => _jobsApiClient.CreateJob(job));
            }
            catch (Exception ex)
            {
                string errorMessage = $"Failed to create job of type '{job.JobDefinitionId}' on specification '{specificationId}'";

                _logger.Error(ex, errorMessage);

                throw new RetriableException(errorMessage, ex);
            }
        }

        private async Task UpdateCalculationInCache(Calculation calculation, CalculationCurrentVersion currentVersion)
        {
            // Invalidate cached calculations for this specification
            await _cachePolicy.ExecuteAsync(() => _cacheProvider.KeyDeleteAsync<List<CalculationSummaryModel>>($"{CacheKeys.CalculationsSummariesForSpecification}{calculation.SpecificationId}"));
            await _cachePolicy.ExecuteAsync(() => _cacheProvider.KeyDeleteAsync<List<CalculationCurrentVersion>>($"{CacheKeys.CurrentCalculationsForSpecification}{calculation.SpecificationId}"));

            // Set current version in cache
            await _cachePolicy.ExecuteAsync(() => _cacheProvider.SetAsync($"{CacheKeys.CurrentCalculation}{calculation.Id}", currentVersion, TimeSpan.FromDays(7), true));
        }

        private async Task<HttpStatusCode> UpdateCalculation(Calculation calculation, CalculationVersion calculationVersion, CalculationVersion previousVersion)
        {
            calculationVersion = await _calculationVersionsRepositoryPolicy.ExecuteAsync(() => _calculationVersionRepository.CreateVersion(calculationVersion, previousVersion));

            calculation.Current = calculationVersion;

            HttpStatusCode result = await _calculationRepositoryPolicy.ExecuteAsync(() => _calculationsRepository.UpdateCalculation(calculation));
            if (result == HttpStatusCode.OK)
            {
                await _calculationVersionsRepositoryPolicy.ExecuteAsync(() => _calculationVersionRepository.SaveVersion(calculationVersion));
            }

            return result;
        }
    }
}
