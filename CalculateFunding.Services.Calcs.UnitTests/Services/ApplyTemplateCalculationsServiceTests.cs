using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.ApiClient.Policies;
using CalculateFunding.Common.Caching;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.TemplateMetadata.Enums;
using CalculateFunding.Common.TemplateMetadata.Models;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Specs;
using CalculateFunding.Services.Calcs.Interfaces;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Tests.Common.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using NSubstitute;
using Polly;
using Serilog;
using Calculation = CalculateFunding.Models.Calcs.Calculation;
using TemplateCalculation = CalculateFunding.Common.TemplateMetadata.Models.Calculation;

namespace CalculateFunding.Services.Calcs.Services
{
    [TestClass]
    public class ApplyTemplateCalculationsServiceTests : TemplateMappingTestBase
    {
        private ICreateCalculationService _createCalculationService;
        private ICalculationsRepository _calculationsRepository;
        private ITemplateContentsCalculationQuery _calculationQuery;
        private IApplyTemplateCalculationsJobTrackerFactory _jobTrackerFactory;
        private IApplyTemplateCalculationsJobTracker _jobTracker;
        private IInstructionAllocationJobCreation _instructionAllocationJobCreation;
        private IPoliciesApiClient _policies;
        private ICalculationService _calculationService;
        private ICacheProvider _cacheProvider;

        private string _specificationId;
        private string _fundingStreamId;
        private string _correlationId;
        private string _templateVersion;
        private string _userId;
        private string _userName;
        private Message _message;

        private const string SpecificationId = "specification-id";
        private const string FundingStreamId = "fundingstream-id";
        private const string TemplateVersion = "template-version";
        private const string CorrelationId = "sfa-correlationId";
        private const string UserId = "user-id";
        private const string UserName = "user-name";

        private ApplyTemplateCalculationsService _service;

        [TestInitialize]
        public void SetUp()
        {
            _policies = Substitute.For<IPoliciesApiClient>();
            _createCalculationService = Substitute.For<ICreateCalculationService>();
            _calculationQuery = Substitute.For<ITemplateContentsCalculationQuery>();
            _calculationsRepository = Substitute.For<ICalculationsRepository>();
            _jobTrackerFactory = Substitute.For<IApplyTemplateCalculationsJobTrackerFactory>();
            _jobTracker = Substitute.For<IApplyTemplateCalculationsJobTracker>();
            _instructionAllocationJobCreation = Substitute.For<IInstructionAllocationJobCreation>();
            _calculationService = Substitute.For<ICalculationService>();
            _cacheProvider = Substitute.For<ICacheProvider>();

            _jobTrackerFactory.CreateJobTracker(Arg.Any<Message>())
                .Returns(_jobTracker);

            _jobTracker.NotifyProgress(Arg.Any<int>())
                .Returns(Task.CompletedTask);
            _jobTracker.TryStartTrackingJob()
                .Returns(Task.FromResult(true));
            _jobTracker.CompleteTrackingJob(Arg.Any<string>(), Arg.Any<int>())
                .Returns(Task.CompletedTask);

            _userId = $"{NewRandomString()}_userId";
            _userName = $"{NewRandomString()}_userName";
            _correlationId = $"{NewRandomString()}_correlationId";
            _specificationId = $"{NewRandomString()}_specificationId";
            _fundingStreamId = $"{NewRandomString()}_fundingStreamId";
            _templateVersion = $"{NewRandomString()}_templateVersion";

            _calculationsRepository.UpdateTemplateMapping(Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<TemplateMapping>())
                .Returns(Task.CompletedTask);

            _service = new ApplyTemplateCalculationsService(_createCalculationService,
                _policies,
                new ResiliencePolicies
                {
                    PoliciesApiClient = Policy.NoOpAsync(),
                    CalculationsRepository = Policy.NoOpAsync(),
                    CacheProviderPolicy = Policy.NoOpAsync()
                },
                _calculationsRepository,
                _calculationQuery,
                _jobTrackerFactory,
                _instructionAllocationJobCreation,
                Substitute.For<ILogger>(),
                _calculationService,
                _cacheProvider);
        }

        [TestMethod]
        public void ThrowsExceptionIfNoMessageSupplied()
        {
            ArgumentNullExceptionShouldBeThrown("message");
        }

        [TestMethod]
        public void ThrowsExceptionIfNoSpecificationIdInMessage()
        {
            GivenTheOtherwiseValidMessage(_ => _.WithoutUserProperty(SpecificationId));

            ArgumentNullExceptionShouldBeThrown(SpecificationId);
        }

        [TestMethod]
        public void ThrowsExceptionIfNoFundingStreamIdInMessage()
        {
            GivenTheOtherwiseValidMessage(_ => _.WithoutUserProperty(FundingStreamId));

            ArgumentNullExceptionShouldBeThrown(FundingStreamId);
        }

        [TestMethod]
        public void ThrowsExceptionIfNoTemplateVersionInMessage()
        {
            GivenTheOtherwiseValidMessage(_ => _.WithoutUserProperty(TemplateVersion));

            ArgumentNullExceptionShouldBeThrown(TemplateVersion);
        }

        [TestMethod]
        public void ThrowsExceptionIfCantLocateTemplateMappingForTheSuppliedSpecificationIdAndFundingStreamId()
        {
            GivenAValidMessage();

            ThenAnExceptionShouldBeThrownWithMessage(
                $"Did not locate Template Mapping for funding stream id {_fundingStreamId} and specification id {_specificationId}");
        }

        [TestMethod]
        public void ThrowsExceptionIfCantLocateTemplateContentsForTheSuppliedFundingStreamIdAndTemplateVersion()
        {
            GivenAValidMessage();
            AndTheTemplateMapping(NewTemplateMapping());

            ThenAnExceptionShouldBeThrownWithMessage(
                $"Did not locate Template Metadata Contents for funding stream id {_fundingStreamId} and template version {_templateVersion}");
        }

        [TestMethod]
        public void ThrowsExceptionIfCreateCallFailsWhenCreatingMissingCalculations()
        {
            TemplateMappingItem mappingWithMissingCalculation1 = NewTemplateMappingItem();
            TemplateMapping templateMapping = NewTemplateMapping(_ => _.WithItems(mappingWithMissingCalculation1));
            TemplateMetadataContents templateMetadataContents = NewTemplateMetadataContents();
            TemplateCalculation templateCalculationOne = NewTemplateMappingCalculation(_ => _.WithName("template calculation 1"));

            GivenAValidMessage();
            AndTheTemplateMapping(templateMapping);
            AndTheTemplateMetaDataContents(templateMetadataContents);
            AndTheTemplateContentsCalculation(mappingWithMissingCalculation1, templateMetadataContents, templateCalculationOne);

            ThenAnExceptionShouldBeThrownWithMessage("Unable to create new default template calculation for template mapping");
        }

        [TestMethod]
        public async Task CreatesCalculationsIfOnTemplateMappingButDontExistYet()
        {
            TemplateMappingItem mappingWithMissingCalculation1 = NewTemplateMappingItem();
            TemplateMappingItem mappingWithMissingCalculation2 = NewTemplateMappingItem();
            TemplateMappingItem mappingWithMissingCalculation3 = NewTemplateMappingItem();

            TemplateMapping templateMapping = NewTemplateMapping(_ => _.WithItems(mappingWithMissingCalculation1,
                NewTemplateMappingItem(mi => mi.WithCalculationId(NewRandomString())),
                mappingWithMissingCalculation2,
                NewTemplateMappingItem(mi => mi.WithCalculationId(NewRandomString())),

                mappingWithMissingCalculation3,
                NewTemplateMappingItem(mi => mi.WithCalculationId(NewRandomString()))));

            TemplateMetadataContents templateMetadataContents = NewTemplateMetadataContents(_ => _.WithFundingLines(NewFundingLine(fl =>
                fl.WithCalculations(
                    NewTemplateMappingCalculation(c1 => {
                        c1.WithCalculations(NewTemplateMappingCalculation(c4 =>c4.WithTemplateCalculationId(4)));
                        c1.WithTemplateCalculationId(1);
                    }),
                    NewTemplateMappingCalculation(c2 => c2.WithTemplateCalculationId(2)),
                    NewTemplateMappingCalculation(c3 => c3.WithTemplateCalculationId(3))))));
            TemplateCalculation templateCalculationOne = NewTemplateMappingCalculation(_ => _.WithName("template calculation 1"));
            TemplateCalculation templateCalculationTwo = NewTemplateMappingCalculation(_ => _.WithName("template calculation 2"));
            TemplateCalculation templateCalculationThree = NewTemplateMappingCalculation(_ => _.WithName("template calculation 3"));

            string newCalculationId1 = NewRandomString();
            string newCalculationId2 = NewRandomString();
            string newCalculationId3 = NewRandomString();

            GivenAValidMessage();
            AndTheTemplateMapping(templateMapping);
            AndTheTemplateMetaDataContents(templateMetadataContents);
            AndTheCalculationIsCreatedForRequestMatching(_ => _.Name == templateCalculationOne.Name &&
                                                              _.SourceCode == "return 0" &&
                                                              _.SpecificationId == _specificationId &&
                                                              _.FundingStreamId == _fundingStreamId &&
                                                              _.ValueType.GetValueOrDefault()
                                                              == templateCalculationOne.ValueFormat.AsMatchingEnum<CalculationValueType>(),
                NewCalculation(_ => _.WithId(newCalculationId1)));
            AndTheCalculationIsCreatedForRequestMatching(_ => _.Name == templateCalculationTwo.Name &&
                                                              _.SourceCode == "return 0" &&
                                                              _.SpecificationId == _specificationId &&
                                                              _.FundingStreamId == _fundingStreamId &&
                                                              _.ValueType.GetValueOrDefault()
                                                              == templateCalculationTwo.ValueFormat.AsMatchingEnum<CalculationValueType>(),
                NewCalculation(_ => _.WithId(newCalculationId2)));
            AndTheCalculationIsCreatedForRequestMatching(_ => _.Name == templateCalculationThree.Name &&
                                                              _.SourceCode == "return 0" &&
                                                              _.SpecificationId == _specificationId &&
                                                              _.FundingStreamId == _fundingStreamId &&
                                                              _.ValueType.GetValueOrDefault()
                                                              == templateCalculationThree.ValueFormat.AsMatchingEnum<CalculationValueType>(),
                NewCalculation(_ => _.WithId(newCalculationId3)));
            AndTheTemplateContentsCalculation(mappingWithMissingCalculation1, templateMetadataContents, templateCalculationOne);
            AndTheTemplateContentsCalculation(mappingWithMissingCalculation2, templateMetadataContents, templateCalculationTwo);
            AndTheTemplateContentsCalculation(mappingWithMissingCalculation3, templateMetadataContents, templateCalculationThree);

            await WhenTheTemplateCalculationsAreApplied();

            mappingWithMissingCalculation1
                .CalculationId
                .Should().Be(newCalculationId1);

            mappingWithMissingCalculation2
                .CalculationId
                .Should().Be(newCalculationId2);

            mappingWithMissingCalculation3
                .CalculationId
                .Should().Be(newCalculationId3);

            AndTheTemplateMappingWasUpdated(templateMapping, 4);
            AndTheJobsStartWasLogged();
            AndTheProgressNotificationsWereMade(1);
            AndTheJobCompletionWasLogged(4);
            AndACalculationRunWasInitialised();
        }

        [TestMethod]
        public async Task ModifiesCalculationsIfOnTemplateExists()
        {
            uint templateCalculationId1 = (uint)new RandomNumberBetween(1, int.MaxValue);
            uint templateCalculationId2 = (uint)new RandomNumberBetween(1, int.MaxValue);

            string calculationId1 = NewRandomString();
            string calculationId2 = NewRandomString();
            string calculationId3 = NewRandomString();

            string calculationName1 = NewRandomString();
            string calculationName2 = NewRandomString();
            string calculationName3 = NewRandomString();

            string newCalculationName1 = NewRandomString();
            string newCalculationName2 = NewRandomString();

            string newCalculationId1 = NewRandomString();
            string newCalculationId2 = NewRandomString();

            CalculationValueFormat calculationValueFormat1 = CalculationValueFormat.Currency;
            CalculationValueFormat calculationValueFormat2 = CalculationValueFormat.Number;
            CalculationValueType calculationValueType3 = CalculationValueType.Percentage;

            TemplateMappingItem mappingWithMissingCalculation1 = NewTemplateMappingItem();
            TemplateMappingItem mappingWithMissingCalculation2 = NewTemplateMappingItem();
            TemplateMappingItem mappingWithMissingCalculation3 = NewTemplateMappingItem(_ => {
                _.WithCalculationId(calculationId3);
                _.WithName(calculationName3);
            });

            TemplateMapping templateMapping = NewTemplateMapping(_ => _.WithItems(mappingWithMissingCalculation1,
                NewTemplateMappingItem(mi => mi.WithCalculationId(calculationId1).WithTemplateId(templateCalculationId1)),
                mappingWithMissingCalculation2,
                NewTemplateMappingItem(mi => mi.WithCalculationId(calculationId2).WithTemplateId(templateCalculationId2)),
                mappingWithMissingCalculation3));

            TemplateMetadataContents templateMetadataContents = NewTemplateMetadataContents(_ => _.WithFundingLines(NewFundingLine(fl =>
                fl.WithCalculations(
                    NewTemplateMappingCalculation(),
                    NewTemplateMappingCalculation(),
                    NewTemplateMappingCalculation(x => x.WithTemplateCalculationId(templateCalculationId1).WithName(calculationName1).WithValueFormat(calculationValueFormat1)),
                    NewTemplateMappingCalculation(x => x.WithTemplateCalculationId(templateCalculationId2).WithName(calculationName2).WithValueFormat(calculationValueFormat2))))));
            TemplateCalculation templateCalculationOne = NewTemplateMappingCalculation(_ => _.WithName("template calculation 1"));
            TemplateCalculation templateCalculationTwo = NewTemplateMappingCalculation(_ => _.WithName("template calculation 2"));

            List<Calculation> calculations = new List<Calculation>
            {
                NewCalculation(_ => _.WithId(calculationId1)
                                    .WithCurrentVersion(
                                        NewCalculationVersion(x=>x.WithName(newCalculationName1)))),
                NewCalculation(_ => _.WithId(calculationId2)
                                    .WithCurrentVersion(
                                        NewCalculationVersion(x=>x.WithName(newCalculationName2)))),
            };

            Calculation missingCalculation = NewCalculation(_ => _.WithId(calculationId3)
                                    .WithCurrentVersion(
                                        NewCalculationVersion(x =>
                                        {
                                            x.WithName(calculationName3);
                                            x.WithValueType(calculationValueType3);
                                        })));

            GivenAValidMessage();
            AndTheTemplateMapping(templateMapping);
            AndTheTemplateMetaDataContents(templateMetadataContents);
            AndTheCalculationIsCreatedForRequestMatching(_ => _.Name == templateCalculationOne.Name &&
                                                              _.SourceCode == "return 0" &&
                                                              _.SpecificationId == _specificationId &&
                                                              _.FundingStreamId == _fundingStreamId &&
                                                              _.ValueType.GetValueOrDefault()
                                                              == templateCalculationOne.ValueFormat.AsMatchingEnum<CalculationValueType>(),
                NewCalculation(_ => _.WithId(newCalculationId1)));
            AndTheCalculationIsEditedForRequestMatching(_ => _.Name == calculationName1 &&
                                                            _.ValueType.GetValueOrDefault() == calculationValueFormat1.AsMatchingEnum<CalculationValueType>() &&
                                                            _.Description == null &&
                                                            _.SourceCode == null,
                calculationId1);
            AndTheCalculationIsCreatedForRequestMatching(_ => _.Name == templateCalculationTwo.Name &&
                                                              _.SourceCode == "return 0" &&
                                                              _.SpecificationId == _specificationId &&
                                                              _.FundingStreamId == _fundingStreamId &&
                                                              _.ValueType.GetValueOrDefault()
                                                              == templateCalculationTwo.ValueFormat.AsMatchingEnum<CalculationValueType>(),
                NewCalculation(_ => _.WithId(newCalculationId2)));
            AndTheCalculationIsEditedForRequestMatching(_ => _.Name == calculationName2 &&
                                                _.ValueType.GetValueOrDefault() == calculationValueFormat2.AsMatchingEnum<CalculationValueType>() &&
                                                _.Description == null &&
                                                _.SourceCode == null,
                calculationId2);

            AndTheMissingCalculationIsEditedForRequestMatching(_ => _.Name == calculationName3 &&
                                                _.ValueType == calculationValueType3 &&
                                                _.Description == null &&
                                                _.SourceCode == null,
                calculationId3);

            AndTheTemplateContentsCalculation(mappingWithMissingCalculation1, templateMetadataContents, templateCalculationOne);
            AndTheTemplateContentsCalculation(mappingWithMissingCalculation2, templateMetadataContents, templateCalculationTwo);

            AndMissingCalculation(calculationId3, missingCalculation);
            AndTheCalculations(calculations);

            await WhenTheTemplateCalculationsAreApplied();

            mappingWithMissingCalculation1
                .CalculationId
                .Should().Be(newCalculationId1);

            mappingWithMissingCalculation2
                .CalculationId
                .Should().Be(newCalculationId2);

            AndTheTemplateMappingWasUpdated(templateMapping, 3);
            AndTheJobsStartWasLogged();
            AndTheProgressNotificationsWereMade(-1);
            AndTheJobCompletionWasLogged(3);
            AndACalculationRunWasInitialised();
        }

        [TestMethod]
        public async Task DoesNotModifiesCalculationsIfOnTemplateExistsAndHasSameValues()
        {
            uint templateCalculationId1 = (uint)new RandomNumberBetween(1, int.MaxValue);
            uint templateCalculationId2 = (uint)new RandomNumberBetween(1, int.MaxValue);
            uint templateCalculationId3 = (uint)new RandomNumberBetween(1, int.MaxValue);
            uint templateCalculationId4 = (uint)new RandomNumberBetween(1, int.MaxValue);

            string calculationId1 = NewRandomString();
            string calculationId2 = NewRandomString();

            string calculationName1 = NewRandomString();
            string calculationName2 = NewRandomString();

            string newCalculationId1 = NewRandomString();
            string newCalculationId2 = NewRandomString();

            CalculationValueFormat calculationValueFormat1 = CalculationValueFormat.Currency;
            CalculationValueFormat calculationValueFormat2 = CalculationValueFormat.Number;

            TemplateMappingItem mappingWithMissingCalculation1 = NewTemplateMappingItem();
            TemplateMappingItem mappingWithMissingCalculation2 = NewTemplateMappingItem();

            TemplateMapping templateMapping = NewTemplateMapping(_ => _.WithItems(mappingWithMissingCalculation1,
                NewTemplateMappingItem(mi => mi.WithCalculationId(calculationId1).WithTemplateId(templateCalculationId1)),
                mappingWithMissingCalculation2,
                NewTemplateMappingItem(mi => mi.WithCalculationId(calculationId2).WithTemplateId(templateCalculationId2))));

            TemplateMetadataContents templateMetadataContents = NewTemplateMetadataContents(_ => _.WithFundingLines(NewFundingLine(fl =>
                fl.WithCalculations(
                    NewTemplateMappingCalculation(x => x.WithTemplateCalculationId(templateCalculationId1).WithName(calculationName1).WithValueFormat(calculationValueFormat1)),
                    NewTemplateMappingCalculation(x => x.WithTemplateCalculationId(templateCalculationId2).WithName(calculationName2).WithValueFormat(calculationValueFormat2)),
                    NewTemplateMappingCalculation(x => x.WithTemplateCalculationId(templateCalculationId3)),
                    NewTemplateMappingCalculation(x => x.WithTemplateCalculationId(templateCalculationId4))))));
            TemplateCalculation templateCalculationOne = NewTemplateMappingCalculation(_ => _.WithName("template calculation 1"));
            TemplateCalculation templateCalculationTwo = NewTemplateMappingCalculation(_ => _.WithName("template calculation 2"));

            List<Calculation> calculations = new List<Calculation>
            {
                NewCalculation(_ => _.WithId(calculationId1)
                                    .WithCurrentVersion(
                                        NewCalculationVersion(x => 
                                            x.WithName(calculationName1).WithValueType(calculationValueFormat1.AsMatchingEnum<CalculationValueType>())))),
                NewCalculation(_ => _.WithId(calculationId2)
                                    .WithCurrentVersion(
                                        NewCalculationVersion(x=>x.WithName(calculationName2).WithValueType(calculationValueFormat2.AsMatchingEnum<CalculationValueType>())))),
            };

            GivenAValidMessage();
            AndTheTemplateMapping(templateMapping);
            AndTheTemplateMetaDataContents(templateMetadataContents);
            AndTheCalculationIsCreatedForRequestMatching(_ => _.Name == templateCalculationOne.Name &&
                                                              _.SourceCode == "return 0" &&
                                                              _.SpecificationId == _specificationId &&
                                                              _.FundingStreamId == _fundingStreamId &&
                                                              _.ValueType.GetValueOrDefault()
                                                              == templateCalculationOne.ValueFormat.AsMatchingEnum<CalculationValueType>(),
                NewCalculation(_ => _.WithId(newCalculationId1)));
            AndTheCalculationIsCreatedForRequestMatching(_ => _.Name == templateCalculationTwo.Name &&
                                                              _.SourceCode == "return 0" &&
                                                              _.SpecificationId == _specificationId &&
                                                              _.FundingStreamId == _fundingStreamId &&
                                                              _.ValueType.GetValueOrDefault()
                                                              == templateCalculationTwo.ValueFormat.AsMatchingEnum<CalculationValueType>(),
                NewCalculation(_ => _.WithId(newCalculationId2)));
            AndTheTemplateContentsCalculation(mappingWithMissingCalculation1, templateMetadataContents, templateCalculationOne);
            AndTheTemplateContentsCalculation(mappingWithMissingCalculation2, templateMetadataContents, templateCalculationTwo);

            AndTheCalculations(calculations);

            await WhenTheTemplateCalculationsAreApplied();

            mappingWithMissingCalculation1
                .CalculationId
                .Should().Be(newCalculationId1);

            mappingWithMissingCalculation2
                .CalculationId
                .Should().Be(newCalculationId2);

            AndTheTemplateMappingWasUpdated(templateMapping, 3);
            AndTheJobsStartWasLogged();
            AndTheProgressNotificationsWereMade(0);
            AndTheJobCompletionWasLogged(4);
            AndACalculationRunWasInitialised();

            AndCalculationEdited(_ => _.Name == calculationName1 &&
                                                            _.ValueType.GetValueOrDefault() == calculationValueFormat1.AsMatchingEnum<CalculationValueType>() &&
                                                            _.Description == null &&
                                                            _.SourceCode == null,
                calculationId1, 0);

            AndCalculationEdited(_ => _.Name == calculationName2 &&
                                                _.ValueType.GetValueOrDefault() == calculationValueFormat2.AsMatchingEnum<CalculationValueType>() &&
                                                _.Description == null &&
                                                _.SourceCode == null,
                calculationId2, 0);
        }

        private void AndTheCalculationIsCreatedForRequestMatching(Expression<Predicate<CalculationCreateModel>> createModelMatching, Calculation calculation)
        {
            _createCalculationService.CreateCalculation(Arg.Is(_specificationId),
                    Arg.Is(createModelMatching),
                    Arg.Is(CalculationNamespace.Template),
                    Arg.Is(Models.Calcs.CalculationType.Template),
                    Arg.Is<Reference>(_ => _.Id == _userId &&
                                           _.Name == _userName),
                    Arg.Is(_correlationId),
                    Arg.Is(false))
                .Returns(new CreateCalculationResponse
                {
                    Succeeded = true,
                    Calculation = calculation
                });
        }

        private void AndTheCalculationIsEditedForRequestMatching(Expression<Predicate<CalculationEditModel>> editModelMatching, string calculationId)
        {
            _calculationService.EditCalculation(Arg.Is(_specificationId),
                Arg.Is(calculationId),
                Arg.Is(editModelMatching),
                Arg.Is<Reference>(_ => _.Id == _userId &&
                                           _.Name == _userName),
                Arg.Is(_correlationId))
                .Returns(new OkObjectResult(null));
        }

        private void AndTheMissingCalculationIsEditedForRequestMatching(Expression<Predicate<CalculationEditModel>> editModelMatching, string calculationId)
        {
            _calculationService.EditCalculation(Arg.Is(_specificationId),
                Arg.Is(calculationId),
                Arg.Is(editModelMatching),
                Arg.Is<Reference>(_ => _.Id == _userId &&
                                           _.Name == _userName),
                Arg.Is(_correlationId),
                Arg.Is(true))
                .Returns(new OkObjectResult(null));
        }

        private void ThenAnExceptionShouldBeThrownWithMessage(string expectedMessage)
        {
            Func<Task> invocation = WhenTheTemplateCalculationsAreApplied;

            invocation
                .Should().Throw<Exception>()
                .WithMessage(expectedMessage);
        }

        private void ArgumentNullExceptionShouldBeThrown(string parameterName)
        {
            Func<Task> invocation = WhenTheTemplateCalculationsAreApplied;

            invocation
                .Should().Throw<ArgumentNullException>()
                .And.ParamName
                .Should().Be(parameterName);
        }

        private void GivenAValidMessage()
        {
            GivenTheOtherwiseValidMessage();
        }

        private void GivenTheOtherwiseValidMessage(Action<MessageBuilder> overrides = null)
        {
            MessageBuilder messageBuilder = new MessageBuilder()
                .WithUserProperty(SpecificationId, _specificationId)
                .WithUserProperty(CorrelationId, _correlationId)
                .WithUserProperty(FundingStreamId, _fundingStreamId)
                .WithUserProperty(TemplateVersion, _templateVersion)
                .WithUserProperty(UserId, _userId)
                .WithUserProperty(UserName, _userName);

            overrides?.Invoke(messageBuilder);

            _message = messageBuilder.Build();
        }

        private void AndTheTemplateMapping(TemplateMapping templateMapping)
        {
            _calculationsRepository.GetTemplateMapping(_specificationId, _fundingStreamId)
                .Returns(templateMapping);
        }

        private void AndTheTemplateMetaDataContents(TemplateMetadataContents templateMetadataContents)
        {
            _policies.GetFundingTemplateContents(_fundingStreamId, _templateVersion)
                .Returns(new ApiResponse<TemplateMetadataContents>(HttpStatusCode.OK, templateMetadataContents));
        }

        private async Task WhenTheTemplateCalculationsAreApplied()
        {
            await _service.ApplyTemplateCalculation(_message);
        }

        private void AndTheTemplateContentsCalculation(TemplateMappingItem mappingItem,
            TemplateMetadataContents templateMetadataContents,
            TemplateCalculation calculation)
        {
            _calculationQuery.GetTemplateContentsForMappingItem(mappingItem, templateMetadataContents)
                .Returns(calculation);
        }

        private void AndTheCalculations(IEnumerable<Calculation> calculations)
        {
            _calculationsRepository.GetCalculationsBySpecificationId(_specificationId)
                .Returns(calculations);
        }

        private void AndMissingCalculation(string calculationId, Calculation calculation)
        {
            _calculationsRepository.GetCalculationById(calculationId)
                .Returns(calculation);
        }

        private void AndTheTemplateMappingWasUpdated(TemplateMapping templateMapping, int numberOfCalls)
        {
            _calculationsRepository.Received(numberOfCalls)
                .UpdateTemplateMapping(_specificationId, _fundingStreamId, templateMapping);
        }

        private void AndTheJobsStartWasLogged()
        {
            _jobTracker
                .Received(1)
                .TryStartTrackingJob();
        }

        private void AndTheProgressNotificationsWereMade(params int[] itemCount)
        {
            foreach (int count in itemCount)
                _jobTracker
                    .Received(1)
                    .NotifyProgress(count);
        }

        private void AndTheJobCompletionWasLogged(int itemCount)
        {
            _jobTracker
                .Received(1)
                .CompleteTrackingJob("Completed Successfully", itemCount);
        }

        private void AndCalculationEdited(Expression<Predicate<CalculationEditModel>> editModelMatching, string calculationId, int requiredNumberOfCalls)
        {
            _calculationService
                .Received(requiredNumberOfCalls)
                .EditCalculation(Arg.Is(_specificationId),
                    Arg.Is(calculationId),
                    Arg.Is(editModelMatching),
                    Arg.Is<Reference>(_ => _.Id == _userId &&
                                           _.Name == _userName),
                    Arg.Is(_correlationId));
        }

        private void AndACalculationRunWasInitialised()
        {
            _instructionAllocationJobCreation
                .Received(1)
                .SendInstructAllocationsToJobService(_specificationId,
                    _userId,
                    _userName,
                    Arg.Is<Trigger>(_ => _.Message == "Assigned Template Calculations" &&
                                         _.EntityId == _specificationId &&
                                         _.EntityType == nameof(Specification))
                    , _correlationId);
        }
    }
}