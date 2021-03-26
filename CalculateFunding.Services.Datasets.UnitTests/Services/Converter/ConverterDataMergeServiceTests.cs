﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.ApiClient.Policies;
using CalculateFunding.Common.ApiClient.Policies.Models.FundingConfig;
using CalculateFunding.Common.ApiClient.Specifications;
using CalculateFunding.Common.ApiClient.Specifications.Models;
using CalculateFunding.Common.JobManagement;
using CalculateFunding.Common.Models;
using CalculateFunding.Models.Datasets;
using CalculateFunding.Models.Datasets.Converter;
using CalculateFunding.Models.Datasets.Schema;
using CalculateFunding.Services.Core;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Datasets.Builders;
using CalculateFunding.Services.Datasets.Converter;
using CalculateFunding.Services.Datasets.Interfaces;
using CalculateFunding.Tests.Common.Builders;
using CalculateFunding.Tests.Common.Helpers;
using CalculateFunding.UnitTests.ApiClientHelpers.Policies;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly;
using Serilog.Core;

namespace CalculateFunding.Services.Datasets.Services
{
    [TestClass]
    public class ConverterDataMergeServiceTests
    {
        private Mock<IValidator<ConverterMergeRequest>> _requestValidation;
        private Mock<IDatasetRepository> _datasets;
        private Mock<IConverterEligibleProviderService> _eligibleProviders;
        private Mock<IPoliciesApiClient> _policies;
        private Mock<ISpecificationsApiClient> _specifications;
        private Mock<IConverterDataMergeLogger> _logs;
        private Mock<IDatasetCloneBuilderFactory> _datasetCloneBuilderFactory;
        private Mock<IDatasetCloneBuilder> _datasetCloneBuilder;
        private Mock<IJobManagement> _jobs;

        private ConverterDataMergeService _converterDataMerge;

        [TestInitialize]
        public void SetUp()
        {
            _requestValidation = new Mock<IValidator<ConverterMergeRequest>>();
            _datasets = new Mock<IDatasetRepository>();
            _eligibleProviders = new Mock<IConverterEligibleProviderService>();
            _policies = new Mock<IPoliciesApiClient>();
            _specifications = new Mock<ISpecificationsApiClient>();
            _logs = new Mock<IConverterDataMergeLogger>();
            _datasetCloneBuilderFactory = new Mock<IDatasetCloneBuilderFactory>();
            _jobs = new Mock<IJobManagement>();
            _datasetCloneBuilder = new Mock<IDatasetCloneBuilder>();

            _datasetCloneBuilderFactory.Setup(_ => _.CreateCloneBuilder())
                .Returns(_datasetCloneBuilder.Object);

            _converterDataMerge = new ConverterDataMergeService(_datasets.Object,
                _eligibleProviders.Object,
                _policies.Object,
                _logs.Object,
                _specifications.Object,
                _datasetCloneBuilderFactory.Object,
                _requestValidation.Object,
                new DatasetsResiliencePolicies
                {
                    DatasetRepository = Policy.NoOpAsync(),
                    PoliciesApiClient = Policy.NoOpAsync(),
                    SpecificationsApiClient = Policy.NoOpAsync()
                },
                _jobs.Object,
                Logger.None);
        }

        [TestMethod]
        public async Task QueueJobRespondsWithBadRequestWhenQueueingJobWithInvalidRequest()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            GivenTheValidationResult(request, NewValidationResult(_ => _.WithValidationFailures(NewValidationFailure())));

            BadRequestObjectResult badRequest = await WhenTheMergeRequestIsQueued(request) as BadRequestObjectResult;

            badRequest
                .Should()
                .NotBeNull();
        }

        [TestMethod]
        public async Task QueueJobCreatesConverterDatasetMergeJobsWithValidRequests()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Job expectedJob = new Job();

            GivenTheValidationResult(request, NewValidationResult());
            AndTheJob(new JobCreateModel
                {
                    JobDefinitionId = JobConstants.DefinitionNames.RunConverterDatasetMergeJob,
                    Trigger = new Trigger
                    {
                        Message = "Converter Merge Dataset Requested",
                        EntityId = request.DatasetRelationshipId,
                        EntityType = "DatasetRelationship"
                    },
                    MessageBody = request.AsJson(),
                    Properties = new Dictionary<string, string>
                    {
                        {
                            "dataset-relationship-id", request.DatasetRelationshipId
                        }
                    }
                },
                expectedJob);

            OkObjectResult result = await WhenTheMergeRequestIsQueued(request) as OkObjectResult;

            result?.Value
                .Should()
                .BeSameAs(expectedJob);
        }

        [TestMethod]
        [DynamicData(nameof(InvalidRequestExamples), DynamicDataSourceType.Method)]
        public void ProcessFailsIfRequestInMessageIsNotValid(ConverterMergeRequest request,
            string expectedErrorMessage)
        {
            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request?
                    .AsJsonBytes())));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be(expectedErrorMessage);
        }

        [TestMethod]
        public void ProcessFailsIfFailsToLocateDatasetForIdSuppliedInRequest()
        {
            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(NewConverterMergeRequest()
                    .AsJsonBytes())));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be("Dataset not found.");
        }

        [TestMethod]
        public void ProcessFailsIfTheDatasetForIdSuppliedInRequestHasNoDefinition()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            GivenTheDataset(request.DatasetId, NewDataset());

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be("Dataset has no definition.");
        }

        [TestMethod]
        public void ProcessFailsIfTheDatasetDefinitionForDatasetIdSuppliedInRequestIsNotFound()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be($"Did not locate dataset definition {datasetDefinitionVersion.Id}");
        }

        [TestMethod]
        public void ProcessFailsIfTheDatasetDefinitionForDatasetIdSuppliedInRequestDoesntAllowConverters()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));
            AndTheDatasetDefinition(datasetDefinitionVersion.Id, NewDatasetDefinition(_ => _.WithConverterEnabled(false)));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be("Dataset is not enabled for converters. Enable it in the dataset definition.");
        }

        [TestMethod]
        public void ProcessFailsIfFieldDefinitionIsMissingIfDatasetDefinition()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));
            AndTheDatasetDefinition(datasetDefinitionVersion.Id, NewDatasetDefinition(_ => _.WithConverterEnabled(true)));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be("No identifier field was specified on this dataset definition.");
        }

        [TestMethod]
        public void ProcessFailsIfFieldDefinitionIsNotUkprn()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));
            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(new RandomEnum<IdentifierFieldType>(IdentifierFieldType.UKPRN))))))));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be("Converter data merge only supports schemas with UKPRN set as the identifier.");
        }

        [TestMethod]
        public void ProcessFailsIfCantLocateRelationshipForIdSuppliedInRequest()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));
            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be($"Dataset relationship not found. Id = '{request.DatasetRelationshipId}'");
        }

        [TestMethod]
        public void ProcessFailsIfDataRelationshipForIdSuppliedHasNotSpecificationDetails()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));
            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            DefinitionSpecificationRelationship relationship = NewDefinitionSpecificationRelationship();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId, relationship);

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be($"DefinitionSpecificationRelationship {relationship.Id} has no specification reference.");
        }

        [TestMethod]
        public void ProcessFailsIfCantLocateSpecificationSummaryForIdOnRelationship()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));
            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            string specificationId = NewRandomString();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId,
                NewDefinitionSpecificationRelationship(_ => _
                    .WithSpecification(NewReference(spec => spec
                        .WithId(specificationId)))));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be($"Did not locate s specification summary for id {specificationId}");
        }

        [TestMethod]
        public void ProcessFailsIfCantLocateFundingConfigurationToMatchRequestDetailsAndLookUps()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));

            string fundingStreamId = NewRandomString();

            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithFundingStreamId(fundingStreamId)
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            string specificationId = NewRandomString();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId,
                NewDefinitionSpecificationRelationship(_ => _
                    .WithSpecification(NewReference(spec => spec
                        .WithId(specificationId)))));

            string fundingPeriodId = NewRandomString();

            AndTheSpecificationSummary(specificationId, NewSpecificationSummary(_ => _.WithFundingPeriodId(fundingPeriodId)));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be($"Did not locate funding configuration for {fundingStreamId} {fundingPeriodId}");
        }

        [TestMethod]
        public void ProcessFailsIfTheFundingConfigurationMatchingTheRequestDetailsAndLookUpsDoesNotAllowConverters()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));

            string fundingStreamId = NewRandomString();

            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithFundingStreamId(fundingStreamId)
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            string specificationId = NewRandomString();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId,
                NewDefinitionSpecificationRelationship(_ => _
                    .WithSpecification(NewReference(spec => spec
                        .WithId(specificationId)))));

            string fundingPeriodId = NewRandomString();

            AndTheSpecificationSummary(specificationId, NewSpecificationSummary(_ => _.WithFundingPeriodId(fundingPeriodId)));
            AndTheFundingConfiguration(fundingStreamId,
                fundingPeriodId,
                NewFundingConfiguration(_ => _.WithEnableConverterDataMerge(false)
                    .WithFundingPeriodId(fundingPeriodId)
                    .WithFundingStreamId(fundingStreamId)));

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be($"Converter data merge not enabled for funding stream {fundingStreamId} and funding period {fundingPeriodId}");
        }

        [TestMethod]
        public void ProcessFailsIfGetsANullEligibleConvertersResponseForTheRequestDetailsAndLookUps()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();

            Func<Task> invocation = () => WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));

            string fundingStreamId = NewRandomString();

            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithFundingStreamId(fundingStreamId)
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            string specificationId = NewRandomString();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId,
                NewDefinitionSpecificationRelationship(_ => _
                    .WithSpecification(NewReference(spec => spec
                        .WithId(specificationId)))));

            string fundingPeriodId = NewRandomString();

            AndTheSpecificationSummary(specificationId, NewSpecificationSummary(_ => _.WithFundingPeriodId(fundingPeriodId)));

            FundingConfiguration fundingConfiguration = NewFundingConfiguration(_ => _.WithEnableConverterDataMerge(true)
                .WithFundingPeriodId(fundingPeriodId)
                .WithFundingStreamId(fundingStreamId));

            AndTheFundingConfiguration(fundingStreamId, fundingPeriodId, fundingConfiguration);
            AndTheEligibleProviders(request.ProviderVersionId, fundingConfiguration, null);

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .Result
                .Which
                .Message
                .Should()
                .Be("Eligible providers returned null");
        }

        [TestMethod]
        public async Task ProcessExitsEarlyIfThereAreNoEligibleProvidersForTheRequest()
        {
            ConverterMergeRequest request = NewConverterMergeRequest();
            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();

            GivenTheDataset(request.DatasetId, NewDataset(_ => _.WithDefinition(datasetDefinitionVersion)));

            string fundingStreamId = NewRandomString();

            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithFundingStreamId(fundingStreamId)
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            string specificationId = NewRandomString();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId,
                NewDefinitionSpecificationRelationship(_ => _
                    .WithSpecification(NewReference(spec => spec
                        .WithId(specificationId)))));

            string fundingPeriodId = NewRandomString();

            AndTheSpecificationSummary(specificationId, NewSpecificationSummary(_ => _.WithFundingPeriodId(fundingPeriodId)));

            FundingConfiguration fundingConfiguration = NewFundingConfiguration(_ => _.WithEnableConverterDataMerge(true)
                .WithFundingPeriodId(fundingPeriodId)
                .WithFundingStreamId(fundingStreamId));

            AndTheFundingConfiguration(fundingStreamId, fundingPeriodId, fundingConfiguration);
            AndTheEligibleProviders(request.ProviderVersionId, fundingConfiguration, new EligibleConverter[0]);

            await WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())));

            ThenNoDatasetDataIsCloned();
        }

        [TestMethod]
        public async Task ProcessCopiesRowForAllEligibleProvidersAndCreatesANewDatasetVersionWithTheResults()
        {
            Reference author = NewReference();
            ConverterMergeRequest request = NewConverterMergeRequest(_ => _.WithAuthor(author));
            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();
            Dataset dataset = NewDataset(_ => _.WithDefinition(datasetDefinitionVersion));

            GivenTheDataset(request.DatasetId, dataset);

            string fundingStreamId = NewRandomString();
            string identifierName = NewRandomString();

            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithFundingStreamId(fundingStreamId)
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithName(identifierName)
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            string specificationId = NewRandomString();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId,
                NewDefinitionSpecificationRelationship(_ => _
                    .WithSpecification(NewReference(spec => spec
                        .WithId(specificationId)))));

            string fundingPeriodId = NewRandomString();

            AndTheSpecificationSummary(specificationId, NewSpecificationSummary(_ => _.WithFundingPeriodId(fundingPeriodId)));

            FundingConfiguration fundingConfiguration = NewFundingConfiguration(_ => _.WithEnableConverterDataMerge(true)
                .WithFundingPeriodId(fundingPeriodId)
                .WithFundingStreamId(fundingStreamId));

            AndTheFundingConfiguration(fundingStreamId, fundingPeriodId, fundingConfiguration);

            EligibleConverter eligibleProviderOne = NewEligibleConverter();
            EligibleConverter eligibleProviderTwo = NewEligibleConverter();
            EligibleConverter eligibleProviderThree = NewEligibleConverter();
            EligibleConverter eligibleProviderFour = NewEligibleConverter();

            AndTheEligibleProviders(request.ProviderVersionId,
                fundingConfiguration,
                new[]
                {
                    eligibleProviderOne,
                    eligibleProviderTwo,
                    eligibleProviderThree,
                    eligibleProviderFour
                });

            string existingIdentifierTwo = eligibleProviderTwo.PreviousProviderIdentifier;
            string existingIdentifierFour = eligibleProviderFour.PreviousProviderIdentifier;

            AndTheExistingIdentifierValues(identifierName,
                new[]
                {
                    existingIdentifierTwo,
                    existingIdentifierFour
                });

            RowCopyResult rowCopyResultOne = NewRowCopyResult();
            RowCopyResult rowCopyResultTwo = NewRowCopyResult(_ => _.WithOutcome(RowCopyOutcome.Copied));

            AndTheRowCopyResult(eligibleProviderTwo, rowCopyResultOne);
            AndTheRowCopyResult(eligibleProviderFour, rowCopyResultTwo);

            DatasetVersion createdDatasetVersion = NewDatasetVersion();

            AndTheNewDatasetVersion(createdDatasetVersion, author);

            string jobId = NewRandomString();

            await WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())
                .WithUserProperty("jobId", jobId)));

            ThenTheOriginalDatasetWasLoaded(dataset);
            AndTheRowCopyResultsAreSaved(author);
            AndTheMergeWasLogged(createdDatasetVersion, request, jobId, rowCopyResultOne, rowCopyResultTwo);
        }

        [TestMethod]
        public async Task ProcessCopiesRowForAllEligibleProvidersButSavesNoChangesIfNoneMade()
        {
            Reference author = NewReference();
            ConverterMergeRequest request = NewConverterMergeRequest(_ => _.WithAuthor(author));
            DatasetDefinitionVersion datasetDefinitionVersion = NewDatasetDefinitionVersion();
            Dataset dataset = NewDataset(_ => _.WithDefinition(datasetDefinitionVersion));

            GivenTheDataset(request.DatasetId, dataset);

            string fundingStreamId = NewRandomString();
            string identifierName = NewRandomString();

            AndTheDatasetDefinition(datasetDefinitionVersion.Id,
                NewDatasetDefinition(_ => _
                    .WithFundingStreamId(fundingStreamId)
                    .WithConverterEnabled(true)
                    .WithTableDefinitions(NewTableDefinition(tab => tab
                        .WithFieldDefinitions(NewFieldDefinition(fld => fld
                            .WithName(identifierName)
                            .WithIdentifierFieldType(IdentifierFieldType.UKPRN)))))));

            string specificationId = NewRandomString();

            AndTheDefinitionSpecificationRelationship(request.DatasetRelationshipId,
                NewDefinitionSpecificationRelationship(_ => _
                    .WithSpecification(NewReference(spec => spec
                        .WithId(specificationId)))));

            string fundingPeriodId = NewRandomString();

            AndTheSpecificationSummary(specificationId, NewSpecificationSummary(_ => _.WithFundingPeriodId(fundingPeriodId)));

            FundingConfiguration fundingConfiguration = NewFundingConfiguration(_ => _.WithEnableConverterDataMerge(true)
                .WithFundingPeriodId(fundingPeriodId)
                .WithFundingStreamId(fundingStreamId));

            AndTheFundingConfiguration(fundingStreamId, fundingPeriodId, fundingConfiguration);

            EligibleConverter eligibleProviderOne = NewEligibleConverter();
            EligibleConverter eligibleProviderTwo = NewEligibleConverter();
            EligibleConverter eligibleProviderThree = NewEligibleConverter();
            EligibleConverter eligibleProviderFour = NewEligibleConverter();

            AndTheEligibleProviders(request.ProviderVersionId,
                fundingConfiguration,
                new[]
                {
                    eligibleProviderOne,
                    eligibleProviderTwo,
                    eligibleProviderThree,
                    eligibleProviderFour
                });

            string existingIdentifierTwo = eligibleProviderTwo.PreviousProviderIdentifier;
            string existingIdentifierFour = eligibleProviderFour.PreviousProviderIdentifier;

            AndTheExistingIdentifierValues(identifierName,
                new[]
                {
                    existingIdentifierTwo,
                    existingIdentifierFour
                });

            RowCopyResult rowCopyResultOne = NewRowCopyResult();
            RowCopyResult rowCopyResultThree = NewRowCopyResult();

            AndTheRowCopyResult(eligibleProviderTwo, rowCopyResultOne);
            AndTheRowCopyResult(eligibleProviderFour, rowCopyResultThree);

            string jobId = NewRandomString();

            await WhenTheMergeJobIsRun(NewMessage(_ => _
                .WithMessageBody(request
                    .AsJsonBytes())
                .WithUserProperty("jobId", jobId)));

            ThenTheOriginalDatasetWasLoaded(dataset);
            AndTheRowCopyResultsAreNotSaved();
        }

        private static IEnumerable<object[]> InvalidRequestExamples()
        {
            yield return new object[]
            {
                null,
                "No ConverterMergeRequest supplied to process"
            };
            yield return new object[]
            {
                NewConverterMergeRequest(_ => _.WithoutProviderVersionId()),
                "Empty or null providerVersionId"
            };
            yield return new object[]
            {
                NewConverterMergeRequest(_ => _.WithoutVersion()),
                "Empty or null version"
            };
            yield return new object[]
            {
                NewConverterMergeRequest(_ => _.WithoutDatasetId()),
                "Empty or null datasetId"
            };
            yield return new object[]
            {
                NewConverterMergeRequest(_ => _.WithoutDatasetRelationshipId()),
                "Empty or null datasetRelationshipId"
            };
        }

        private async Task WhenTheMergeJobIsRun(Message message)
            => await _converterDataMerge.Process(message);

        private async Task<IActionResult> WhenTheMergeRequestIsQueued(ConverterMergeRequest converterMergeRequest)
            => await _converterDataMerge.QueueJob(converterMergeRequest);

        private void ThenNoDatasetDataIsCloned()
            => _datasetCloneBuilderFactory.Verify(_ => _.CreateCloneBuilder(), Times.Never);

        private void ThenTheOriginalDatasetWasLoaded(Dataset dataset)
            => _datasetCloneBuilder.Verify(_ => _.LoadOriginalDataset(dataset), Times.Once);

        private void AndTheNewDatasetVersion(DatasetVersion datasetVersion,
            Reference author)
            => _datasetCloneBuilder.Setup(_ => _.SaveContents(It.Is<Reference>(user
                    => AreEquivalent(user, author))))
                .ReturnsAsync(datasetVersion);

        private void AndTheRowCopyResultsAreSaved(Reference author)
            => _datasetCloneBuilder.Verify(_ => _.SaveContents(It.Is<Reference>(user
                    => AreEquivalent(user, author))),
                Times.Once);

        private void AndTheRowCopyResultsAreNotSaved()
            => _datasetCloneBuilder.Verify(_ => _.SaveContents(It.IsAny<Reference>()), Times.Never);

        private void AndTheMergeWasLogged(DatasetVersion createdVersion,
            ConverterMergeRequest request,
            string jobId,
            params RowCopyResult[] results)
            => _logs.Verify(_ => _.SaveLogs(It.Is<IEnumerable<RowCopyResult>>(res =>
                        res.SequenceEqual(results)),
                    It.Is<ConverterMergeRequest>(req => AreEquivalent(req, request)),
                    jobId,
                    createdVersion.Version),
                Times.Once);

        private void GivenTheDataset(string datasetId,
            Dataset dataset)
            => _datasets.Setup(_ => _.GetDatasetByDatasetId(datasetId))
                .ReturnsAsync(dataset);

        private void AndTheDatasetDefinition(string definitionId,
            DatasetDefinition datasetDefinition)
            => _datasets.Setup(_ => _.GetDatasetDefinition(definitionId))
                .ReturnsAsync(datasetDefinition);

        private void AndTheDefinitionSpecificationRelationship(string relationshipId,
            DefinitionSpecificationRelationship relationship)
            => _datasets.Setup(_ => _.GetDefinitionSpecificationRelationshipById(relationshipId))
                .ReturnsAsync(relationship);

        private void AndTheSpecificationSummary(string specificationId,
            SpecificationSummary specificationSummary)
            => _specifications.Setup(_ => _.GetSpecificationSummaryById(specificationId))
                .ReturnsAsync(new ApiResponse<SpecificationSummary>(HttpStatusCode.OK, specificationSummary));

        private void AndTheFundingConfiguration(string fundingStreamId,
            string fundingPeriodId,
            FundingConfiguration fundingConfiguration)
            => _policies.Setup(_ => _.GetFundingConfiguration(fundingStreamId, fundingPeriodId))
                .ReturnsAsync(new ApiResponse<FundingConfiguration>(HttpStatusCode.OK, fundingConfiguration));

        private void AndTheEligibleProviders(string providerVersionId,
            FundingConfiguration fundingConfiguration,
            IEnumerable<EligibleConverter> eligibleProviders)
            => _eligibleProviders.Setup(_ => _.GetProviderIdsForConverters(providerVersionId, fundingConfiguration))
                .ReturnsAsync(eligibleProviders);

        private void AndTheExistingIdentifierValues(string fieldName,
            IEnumerable<string> existingIdentifiers)
            => _datasetCloneBuilder.Setup(_ => _.GetExistingIdentifierValues(fieldName))
                .ReturnsAsync(existingIdentifiers);

        private void AndTheRowCopyResult(EligibleConverter eligibleConverter,
            RowCopyResult rowCopyResult)
            => _datasetCloneBuilder.Setup(_ => _.CopyRow(eligibleConverter.PreviousProviderIdentifier, eligibleConverter.ProviderId))
                .ReturnsAsync(rowCopyResult);

        private void GivenTheValidationResult(ConverterMergeRequest converterMergeRequest,
            ValidationResult validationResult)
            => _requestValidation.Setup(_ => _.ValidateAsync(converterMergeRequest, It.IsAny<CancellationToken>()))
                .ReturnsAsync(validationResult);

        private void AndTheJob(JobCreateModel jobCreateModel,
            Job job)
            => _jobs.Setup(_ => _.QueueJobs(It.Is<IEnumerable<JobCreateModel>>(jobs =>
                    jobs.Count() == 1 &&
                    AreEquivalent(jobs.First(), jobCreateModel)
                )))
                .ReturnsAsync(new[]
                {
                    job
                });

        private bool AreEquivalent<TItem>(TItem actual,
            TItem expected)
        {
            try
            {
                actual
                    .Should()
                    .BeEquivalentTo(expected);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private ValidationResult NewValidationResult(Action<ValidationResultBuilder> setUp = null)
        {
            ValidationResultBuilder validationResultBuilder = new ValidationResultBuilder();

            setUp?.Invoke(validationResultBuilder);

            return validationResultBuilder.Build();
        }

        private ValidationFailure NewValidationFailure() => new ValidationFailureBuilder().Build();

        private FundingConfiguration NewFundingConfiguration(Action<FundingConfigurationBuilder> setUp = null)
        {
            FundingConfigurationBuilder builder = new FundingConfigurationBuilder();

            setUp?.Invoke(builder);

            return builder.Build();
        }

        private Dataset NewDataset(Action<DatasetBuilder> setUp = null)
        {
            DatasetBuilder builder = new DatasetBuilder();

            setUp?.Invoke(builder);

            return builder.Build();
        }

        private RowCopyResult NewRowCopyResult(Action<RowCopyResultBuilder> setUp = null)
        {
            RowCopyResultBuilder builder = new RowCopyResultBuilder();

            setUp?.Invoke(builder);

            return builder.Build();
        }

        private EligibleConverter NewEligibleConverter(Action<EligibleConverterBuilder> setUp = null)
        {
            EligibleConverterBuilder builder = new EligibleConverterBuilder();

            setUp?.Invoke(builder);

            return builder.Build();
        }

        private TableDefinition NewTableDefinition(Action<TableDefinitionBuilder> setUp = null)
        {
            TableDefinitionBuilder builder = new TableDefinitionBuilder();

            setUp?.Invoke(builder);

            return builder.Build();
        }

        private DatasetDefinition NewDatasetDefinition(Action<DatasetDefinitionBuilder> setUp = null)
        {
            DatasetDefinitionBuilder datasetDefinitionBuilder = new DatasetDefinitionBuilder();

            setUp?.Invoke(datasetDefinitionBuilder);

            return datasetDefinitionBuilder.Build();
        }

        private FieldDefinition NewFieldDefinition(Action<FieldDefinitionBuilder> setUp = null)
        {
            FieldDefinitionBuilder datasetDefinitionBuilder = new FieldDefinitionBuilder();

            setUp?.Invoke(datasetDefinitionBuilder);

            return datasetDefinitionBuilder.Build();
        }

        private DefinitionSpecificationRelationship NewDefinitionSpecificationRelationship(Action<DefinitionSpecificationRelationshipBuilder> setUp = null)
        {
            DefinitionSpecificationRelationshipBuilder definitionSpecificationRelationshipBuilder = new DefinitionSpecificationRelationshipBuilder();

            setUp?.Invoke(definitionSpecificationRelationshipBuilder);

            return definitionSpecificationRelationshipBuilder.Build();
        }

        private static ConverterMergeRequest NewConverterMergeRequest(Action<ConverterMergeRequestBuilder> setUp = null)
        {
            ConverterMergeRequestBuilder datasetVersionBuilder = new ConverterMergeRequestBuilder();

            setUp?.Invoke(datasetVersionBuilder);

            return datasetVersionBuilder.Build();
        }

        private SpecificationSummary NewSpecificationSummary(Action<ApiSpecificationSummaryBuilder> setUp = null)
        {
            ApiSpecificationSummaryBuilder specificationSummaryBuilder = new ApiSpecificationSummaryBuilder();

            setUp?.Invoke(specificationSummaryBuilder);

            return specificationSummaryBuilder.Build();
        }

        private Reference NewReference(Action<ReferenceBuilder> setUp = null)
        {
            ReferenceBuilder referenceBuilder = new ReferenceBuilder();

            setUp?.Invoke(referenceBuilder);

            return referenceBuilder.Build();
        }

        private static RandomString NewRandomString() => new RandomString();

        private Message NewMessage(Action<MessageBuilder> setUp = null)
        {
            MessageBuilder messageBuilder = new MessageBuilder();

            setUp?.Invoke(messageBuilder);

            return messageBuilder.Build();
        }

        private DatasetDefinitionVersion NewDatasetDefinitionVersion(Action<DatasetDefinitionVersionBuilder> setUp = null)
        {
            DatasetDefinitionVersionBuilder datasetDefinitionVersionBuilder = new DatasetDefinitionVersionBuilder();

            setUp?.Invoke(datasetDefinitionVersionBuilder);

            return datasetDefinitionVersionBuilder.Build();
        }

        private DatasetVersion NewDatasetVersion() => new DatasetVersionBuilder().Build();
    }
}