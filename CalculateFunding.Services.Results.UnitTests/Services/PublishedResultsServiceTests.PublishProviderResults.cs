﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CalculateFunding.Common.ApiClient.Jobs;
using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.Caching;
using CalculateFunding.Common.FeatureToggles;
using CalculateFunding.Common.Models;
using CalculateFunding.Models;
using CalculateFunding.Models.Results;
using CalculateFunding.Models.Results.Messages;
using CalculateFunding.Models.Specs;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Core.Interfaces;
using CalculateFunding.Services.Core.Interfaces.ServiceBus;
using CalculateFunding.Services.Results.Interfaces;
using FluentAssertions;
using Microsoft.Azure.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Serilog;

namespace CalculateFunding.Services.Results.Services
{
    public partial class PublishedResultsServiceTests
    {
        private const string SpecificationId1 = "specId1";
        private const string RedisPrependKey = "calculation-progress:";

        [TestMethod]
        public void PublishProviderResults_WhenMessageIsNull_ThenArgumentNullExceptionThrown()
        {
            // Arrange
            PublishedResultsService resultsService = CreateResultsService();

            // Act
            Func<Task> test = () => resultsService.PublishProviderResults(null);

            //Assert
            test
                .Should().
                ThrowExactly<ArgumentNullException>()
                .And
                .ParamName
                .Should()
                .Be("message");
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenMessageDoesNotHaveSpecificationId_ThenDoesNotProcess()
        {
            // Arrange
            ILogger logger = CreateLogger();
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();

            PublishedResultsService resultsService = CreateResultsService(specificationsRepository: specificationsRepository, logger: logger);
            Message message = new Message();

            // Act
            await resultsService.PublishProviderResults(message);

            //Assert
            logger
                .Received(1)
                .Error(Arg.Is("No specification Id was provided to PublishProviderResults"));

            await specificationsRepository
                .DidNotReceive()
                .GetCurrentSpecificationById(Arg.Any<string>());
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenSpecificationNotFound_ThenDoesNotProcess()
        {
            // Arrange
            ILogger logger = CreateLogger();
            ICalculationResultsRepository calculationResultsRepository = CreateResultsRepository();

            PublishedResultsService resultsService = CreateResultsService(logger: logger, resultsRepository: calculationResultsRepository);
            Message message = new Message();
            message.UserProperties["specification-id"] = "-1";

            // Act
            await resultsService.PublishProviderResults(message);

            // Assert
            logger
                .Received(1)
                .Error(Arg.Is($"Specification not found for specification id -1"));

            await calculationResultsRepository
                .DidNotReceive()
                .GetProviderResultsBySpecificationId(Arg.Is("-1"), Arg.Any<int>());
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenNoProviderResultsForSpecification_ThenDoesNotContinue()
        {
            // Arrange
            string specificationId = "1";
            IEnumerable<ProviderResult> providerResults = Enumerable.Empty<ProviderResult>();

            ILogger logger = CreateLogger();
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository
                .GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(new SpecificationCurrentVersion { Id = specificationId });

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository
                .GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
            IPublishedProviderResultsAssemblerService resultsAssembler = CreateResultsAssembler();

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                logger: logger,
                publishedProviderResultsAssemblerService: resultsAssembler,
                specificationsRepository: specificationsRepository);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            await resultsService.PublishProviderResults(message);

            // Assert
            logger
                .Received(1)
                .Error(Arg.Is($"Provider results not found for specification id {specificationId}"));
        }

        [TestMethod]
        public void PublishProviderResults_WhenErrorSavingPublishedResults_ThenExceptionThrown()
        {
            // Arrange
            string specificationId = "1";

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>()
            {
               new PublishedProviderResult()
                {
                    ProviderId = "1",
                    FundingPeriod = new Period
                    {
                        Id = "fp-1"
                    },
                    FundingStreamResult = new PublishedFundingStreamResult()
                    {
                        AllocationLineResult = new PublishedAllocationLineResult()
                        {
                            AllocationLine = new AllocationLine()
                            {
                                Id = "AAAAA",
                                Name = "Allocation line 1",
                            },
                            Current = new PublishedAllocationLineResultVersion()
                            {
                                Value = 1,
                                Provider = new ProviderSummary
                                {
                                    UKPRN = "1"
                                }
                            }
                        }
                    }
                },
            };

            IEnumerable<PublishedProviderCalculationResult> publishedProviderCalculationResults = new[]
            {
                new PublishedProviderCalculationResult
                {
                    CalculationSpecification = new Reference { Id = "calc-1", Name = "calc1" }
                }
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);
            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(ex => { throw new Exception("Error saving published results"); });

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();

            assembler
                .GeneratePublishedProviderResultsToSave(Arg.Any<IEnumerable<PublishedProviderResult>>(), Arg.Any<IEnumerable<PublishedProviderResultExisting>>())
                .Returns((publishedProviderResults, Enumerable.Empty<PublishedProviderResultExisting>()));

            PublishedResultsService resultsService = CreateResultsService(
                resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsAssemblerService: assembler);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            Func<Task> test = () => resultsService.PublishProviderResults(message);

            //Assert
            Exception thrownException = test.Should().ThrowExactly<Exception>().Subject.First();
            thrownException.Message.Should().Be($"Failed to create published provider results for specification: {specificationId}");
            thrownException.InnerException.Should().NotBeNull();
            thrownException.InnerException.Message.Should().Be("Error saving published results");
        }

        [TestMethod]
        public void PublishProviderResults_WhenErrorSavingPublishedResultsVersionHistory_ThenExceptionThrown()
        {
            // Arrange
            string specificationId = "1";

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>()
            {
                new PublishedProviderResult()
                {
                    ProviderId = "1",
                    FundingPeriod = new Period
                    {
                        Id = "fp-1"
                    },
                    FundingStreamResult = new PublishedFundingStreamResult()
                    {
                        AllocationLineResult = new PublishedAllocationLineResult()
                        {
                            AllocationLine = new AllocationLine()
                            {
                                Id = "AAAAA",
                                Name = "Allocation line 1",
                            },
                            Current = new PublishedAllocationLineResultVersion()
                            {
                                Value = 1,
                                Provider = new ProviderSummary
                                {
                                    UKPRN = "99999"
                                },
                                Major = 2,
                                Minor = 1
                            }
                        }
                    }
                },
            };

            IEnumerable<PublishedProviderCalculationResult> publishedProviderCalculationResults = new[]
            {
                new PublishedProviderCalculationResult
                {
                    CalculationSpecification = new Reference { Id = "calc-1", Name = "calc1" }
                }
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);
            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<KeyValuePair<string, PublishedAllocationLineResultVersion>>>())
                .Returns(ex => { throw new Exception("Error saving published results version history"); });

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();

            assembler
                .GeneratePublishedProviderResultsToSave(Arg.Any<IEnumerable<PublishedProviderResult>>(), Arg.Any<IEnumerable<PublishedProviderResultExisting>>())
                .Returns((publishedProviderResults, Enumerable.Empty<PublishedProviderResultExisting>()));

            PublishedResultsService resultsService = CreateResultsService(
                resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsAssemblerService: assembler,
                publishedProviderResultsVersionRepository: versionRepository);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            Func<Task> test = () => resultsService.PublishProviderResults(message);

            //Assert
            Exception thrownException = test.Should().ThrowExactly<Exception>().Subject.First();
            thrownException.Message.Should().Be($"Failed to create published provider results for specification: {specificationId}");
            thrownException.InnerException.Should().NotBeNull();
            thrownException.InnerException.Message.Should().Be("Error saving published results version history");

            publishedProviderResults.First().FundingStreamResult.AllocationLineResult.Current.FeedIndexId.Should().Be("AAAAA-fp-1-99999-v2-1");
        }

        [TestMethod]
        public void PublishProviderResults_WhenCompletesSuccessfully_ThenNoExceptionThrown()
        {
            // Arrange
            string specificationId = "1";

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            IEnumerable<PublishedProviderCalculationResult> publishedProviderCalculationResults = new[]
             {
                new PublishedProviderCalculationResult
                {
                    CalculationSpecification = new Reference { Id = "calc-1", Name = "calc1" }
                }
            };

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));

            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);

            specificationsRepository.UpdatePublishedRefreshedDate(Arg.Is(specificationId), Arg.Any<DateTimeOffset>())
                .Returns(Task.FromResult(HttpStatusCode.OK));

            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();

            ILogger logger = CreateLogger();

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsVersionRepository: versionRepository,
                publishedProviderResultsAssemblerService: assembler,
                logger: logger);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            Func<Task> test = () => resultsService.PublishProviderResults(message);

            //Assert
            test.Should().NotThrow();
            logger.DidNotReceive().Error(Arg.Any<string>());
            logger.Received(1).Information(Arg.Is($"Updated the published refresh date on the specification with id: {specificationId}"));
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenCalcResultsAssembled_ThenResultsSentForProfiling()
        {
            // Arrange
            string specificationId = "1";

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            IEnumerable<PublishedProviderCalculationResult> publishedProviderCalculationResults = new[]
            {
                new PublishedProviderCalculationResult
                {
                    CalculationSpecification = new Reference { Id = "calc-1", Name = "calc1" }
                }
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>();
            publishedProviderResults
                .Add(new PublishedProviderResult()
                {
                    FundingStreamResult = new PublishedFundingStreamResult()
                    {
                        AllocationLineResult = new PublishedAllocationLineResult()
                        {
                            Current = new PublishedAllocationLineResultVersion()
                            {
                                Status = AllocationLineStatus.Updated
                            },
                            AllocationLine = new AllocationLine()
                            {
                                Id = "alId",
                                Name = "Allocation Line",
                            },
                        }
                    }
                });

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);
            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();

            IMessengerService messengerService = CreateMessengerService();

            assembler
                .AssemblePublishedProviderResults(Arg.Any<IEnumerable<ProviderResult>>(), Arg.Any<Reference>(), Arg.Is(specificationCurrentVersion))
                .Returns(publishedProviderResults);

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsAssemblerService: assembler,
                publishedProviderResultsVersionRepository: versionRepository,
                messengerService: messengerService);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            await resultsService.PublishProviderResults(message);

            //Assert
            await messengerService
                .Received(1)
                .SendToQueue(ServiceBusConstants.QueueNames.FetchProviderProfile, Arg.Any<IEnumerable<FetchProviderProfilingMessageItem>>(), Arg.Any<IDictionary<string, string>>());
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenCalcResultsAssembled_ThenResultsSentForProfilingInMultipleBatches()
        {
            // Arrange
            string specificationId = "1";

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            IEnumerable<PublishedProviderCalculationResult> publishedProviderCalculationResults = new[]
            {
                new PublishedProviderCalculationResult
                {
                    CalculationSpecification = new Reference { Id = "calc-1", Name = "calc1" }
                }
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>();

            for (int i = 0; i < 560; i++)
            {
                publishedProviderResults
                    .Add(new PublishedProviderResult()
                    {
                        FundingStreamResult = new PublishedFundingStreamResult()
                        {
                            AllocationLineResult = new PublishedAllocationLineResult()
                            {
                                Current = new PublishedAllocationLineResultVersion()
                                {
                                    Status = AllocationLineStatus.Updated
                                },
                                AllocationLine = new AllocationLine()
                                {
                                    Id = "alId",
                                    Name = "Allocation Line",
                                },
                            }
                        }
                    });
            }

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);
            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();

            IMessengerService messengerService = CreateMessengerService();

            assembler
                .AssemblePublishedProviderResults(Arg.Any<IEnumerable<ProviderResult>>(), Arg.Any<Reference>(), Arg.Is(specificationCurrentVersion))
                .Returns(publishedProviderResults);

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsAssemblerService: assembler,
                publishedProviderResultsVersionRepository: versionRepository,
                messengerService: messengerService);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            await resultsService.PublishProviderResults(message);

            //Assert
            await messengerService
                .Received(6)
                .SendToQueue(ServiceBusConstants.QueueNames.FetchProviderProfile, Arg.Any<IEnumerable<FetchProviderProfilingMessageItem>>(), Arg.Any<IDictionary<string, string>>());
        }


        [TestMethod]
        public void PublishProviderResults_WhenNoExceptionThrown_ShouldReportProgressOnCacheCorrectly()
        {
            // Arrange
            string specificationId = SpecificationId1;
            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>()
            {
                new PublishedProviderResult()
                {
                    ProviderId = "1",
                    FundingPeriod = new Period
                    {
                        Id = "fp-1"
                    },
                    FundingStreamResult = new PublishedFundingStreamResult()
                    {
                        AllocationLineResult = new PublishedAllocationLineResult()
                        {
                            AllocationLine = new AllocationLine()
                            {
                                Id = "AAAAA",
                                Name = "Allocation line 1",
                            },
                            Current = new PublishedAllocationLineResultVersion()
                            {
                                Value = 1,
                                Provider = new ProviderSummary
                                {
                                    UKPRN = "99999"
                                },
                                Major = 1,
                                Minor = 1
                            }
                        }
                    }
                },
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));

            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(Task.FromResult(new SpecificationCurrentVersion()));

            IPublishedProviderResultsRepository publishedProviderResultsRepository =
                CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();

            assembler
                .GeneratePublishedProviderResultsToSave(Arg.Any<IEnumerable<PublishedProviderResult>>(), Arg.Any<IEnumerable<PublishedProviderResultExisting>>())
                .Returns((publishedProviderResults, Enumerable.Empty<PublishedProviderResultExisting>()));

            ICacheProvider mockCacheProvider = CreateCacheProvider();

            SpecificationCalculationExecutionStatus expectedProgressCall1 = CreateSpecificationCalculationProgress(c =>
            {
                c.PercentageCompleted = 0;
                c.CalculationProgress = CalculationProgressStatus.InProgress;
            });
            SpecificationCalculationExecutionStatus expectedProgressCall2 = CreateSpecificationCalculationProgress(c => c.PercentageCompleted = 5);
            SpecificationCalculationExecutionStatus expectedProgressCall3 = CreateSpecificationCalculationProgress(c => c.PercentageCompleted = 10);
            SpecificationCalculationExecutionStatus expectedProgressCall4 = CreateSpecificationCalculationProgress(c => c.PercentageCompleted = 28);
            SpecificationCalculationExecutionStatus expectedProgressCall5 = CreateSpecificationCalculationProgress(c => c.PercentageCompleted = 43);
            SpecificationCalculationExecutionStatus expectedProgressCall6 = CreateSpecificationCalculationProgress(c => c.PercentageCompleted = 58);
            SpecificationCalculationExecutionStatus expectedProgressCall7 = CreateSpecificationCalculationProgress(c => c.PercentageCompleted = 73);
            SpecificationCalculationExecutionStatus expectedProgressCall8 = CreateSpecificationCalculationProgress(c => c.PercentageCompleted = 78);
            SpecificationCalculationExecutionStatus expectedProgressCall9 = CreateSpecificationCalculationProgress(c =>
            {
                c.PercentageCompleted = 100;
                c.CalculationProgress = CalculationProgressStatus.Finished;
            });

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                cacheProvider: mockCacheProvider,
                publishedProviderResultsAssemblerService: assembler,
                publishedProviderResultsVersionRepository: versionRepository);

            Message message = new Message();
            message.UserProperties["specification-id"] = SpecificationId1;

            // Act
            Func<Task> publishProviderResultsAction = () => resultsService.PublishProviderResults(message);

            //Assert
            publishProviderResultsAction.Should().NotThrow();

            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall1, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall2, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall3, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall4, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall5, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall6, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall7, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall8, TimeSpan.FromHours(6), false);
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", expectedProgressCall9, TimeSpan.FromHours(6), false);

            mockCacheProvider.Received(9).SetAsync(Arg.Any<string>(), Arg.Any<SpecificationCalculationExecutionStatus>(), Arg.Any<TimeSpan>(), Arg.Any<bool>());

            publishedProviderResults.First().FundingStreamResult.AllocationLineResult.Current.FeedIndexId.Should().Be("AAAAA-fp-1-99999-v1-1");
        }

        [TestMethod]
        public void PublishProviderResults_WhenAnExceptionIsSavingPublishedResults_ThenErrorIsReportedOnCache()
        {
            // Arrange
            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            IEnumerable<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>()
            {
                new PublishedProviderResult()
                {
                    ProviderId = "1",
                    FundingPeriod = new Period
                    {
                        Id = "fp-1"
                    },
                    FundingStreamResult = new PublishedFundingStreamResult()
                    {
                        AllocationLineResult = new PublishedAllocationLineResult()
                        {
                            AllocationLine = new AllocationLine()
                            {
                                Id = "AAAAA",
                                Name = "Allocation line 1",
                            },
                            Current = new PublishedAllocationLineResultVersion()
                            {
                                Value = 1,
                                Provider = new ProviderSummary
                                {
                                    UKPRN = "99999"
                                },
                                Major = 1,
                                Minor = 1
                            }
                        }
                    }
                },
            };

            IEnumerable<PublishedProviderResultExisting> publishedProviderResultExisting = new List<PublishedProviderResultExisting>
            {
                new PublishedProviderResultExisting()
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(SpecificationId1), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
       
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(SpecificationId1))
                .Returns(Task.FromResult(new SpecificationCurrentVersion()));

            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository
                .When(x => x.SavePublishedResults(Arg.Any<List<PublishedProviderResult>>()))
                .Do(x => { throw new Exception("Error saving published calculation results"); });

            IPublishedProviderResultsAssemblerService assemblerService = CreateResultsAssembler();
            assemblerService
                .GeneratePublishedProviderResultsToSave(Arg.Any<IEnumerable<PublishedProviderResult>>(), Arg.Any<IEnumerable<PublishedProviderResultExisting>>())
                .Returns((publishedProviderResults, publishedProviderResultExisting));

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            ICacheProvider mockCacheProvider = CreateCacheProvider();

            SpecificationCalculationExecutionStatus expectedErrorProgress = CreateSpecificationCalculationProgress(c =>
            {
                c.PercentageCompleted = 15;
                c.CalculationProgress = CalculationProgressStatus.Error;
                c.ErrorMessage = "Failed to create published provider calculation results";
            });

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                cacheProvider: mockCacheProvider,
                publishedProviderResultsAssemblerService: assemblerService,
                publishedProviderResultsVersionRepository: versionRepository);

            Message message = new Message();
            message.UserProperties["specification-id"] = SpecificationId1;

            // Act
            Func<Task> publishProviderAction = () => resultsService.PublishProviderResults(message);

            //Assert
            publishProviderAction.Should().ThrowExactly<Exception>();
            mockCacheProvider.Received().SetAsync($"{RedisPrependKey}{SpecificationId1}", Arg.Any<SpecificationCalculationExecutionStatus>(), TimeSpan.FromHours(6), false);
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenNoExisitingAllocationResultsPublished_ThenResultsAreSaved()
        {
            // Arrange
            string specificationId = "spec-1";
            string calculationId = "calc-1";
            string providerId = "prov-1";
            Reference author = new Reference("author-1", "author1");
            Period fundingPeriod = new Period()
            {
                Id = "fp1",
                Name = "Funding Period 1",
            };

            string resultId = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{specificationId}{providerId}{calculationId}"));

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            IEnumerable<PublishedProviderCalculationResult> publishedProviderCalculationResults = Enumerable.Empty<PublishedProviderCalculationResult>();

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            AllocationLine allocationLine1 = new AllocationLine()
            {
                Id = "AAAAA",
                Name = "Allocation Line 1",
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>();
            publishedProviderResults.Add(new PublishedProviderResult()
            {
                FundingPeriod = fundingPeriod,
                ProviderId = providerId,
                SpecificationId = specificationId,
                FundingStreamResult = new PublishedFundingStreamResult()
                {
                    AllocationLineResult = new PublishedAllocationLineResult()
                    {
                        AllocationLine = allocationLine1,
                        Current = new PublishedAllocationLineResultVersion()
                        {
                            Author = author,
                            Status = AllocationLineStatus.Held,
                            Value = 1,
                            Provider = new ProviderSummary
                            {
                                UKPRN = "1234"
                            }
                        }
                    }
                }
            });

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);
            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();
            assembler
                .AssemblePublishedProviderResults(Arg.Any<IEnumerable<ProviderResult>>(), Arg.Any<Reference>(), Arg.Any<SpecificationCurrentVersion>())
                .Returns(publishedProviderResults);

            assembler
                .GeneratePublishedProviderResultsToSave(Arg.Any<IEnumerable<PublishedProviderResult>>(), Arg.Any<IEnumerable<PublishedProviderResultExisting>>())
                .Returns((publishedProviderResults, Enumerable.Empty<PublishedProviderResultExisting>()));

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsAssemblerService: assembler);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            await resultsService.PublishProviderResults(message);

            //Assert
            await
                publishedProviderResultsRepository
                    .Received(1)
                    .SavePublishedResults(Arg.Is<IEnumerable<PublishedProviderResult>>(a =>
                    a.First().ProviderId == providerId &&
                    a.First().FundingStreamResult.AllocationLineResult.Current.Value == 1 &&
                    a.First().FundingStreamResult.AllocationLineResult.Current.Status == AllocationLineStatus.Held));

            await
                publishedProviderResultsRepository
                    .Received(1)
                    .SavePublishedResults(Arg.Is<IEnumerable<PublishedProviderResult>>(a => a.Count() == 1));
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenExisitingAllocationResultsShouldBeExcluded_ThenResultsAreSaved()
        {
            // Arrange
            string specificationId = "spec-1";
            string calculationId = "calc-1";
            string providerId = "prov-1";
            Reference author = new Reference("author-1", "author1");
            Period fundingPeriod = new Period()
            {
                Id = "fp1",
                Name = "Funding Period 1",
            };

            string resultId = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{specificationId}{providerId}{calculationId}"));

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            IEnumerable<PublishedProviderCalculationResult> publishedProviderCalculationResults = Enumerable.Empty<PublishedProviderCalculationResult>();

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            AllocationLine allocationLine1 = new AllocationLine()
            {
                Id = "AAAAA",
                Name = "Allocation Line 1",
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>();

            List<PublishedProviderResultExisting> existingToRemove = new List<PublishedProviderResultExisting>()
            {
                new PublishedProviderResultExisting()
                {
                    Id = "c3BlYy0xMTIzQUFBQUE=",
                    AllocationLineId = allocationLine1.Id,
                    ProviderId = "123",
                    Status = AllocationLineStatus.Approved,
                    Value = 51,
                }
            };

            PublishedProviderResult existingProviderResultToRemove = new PublishedProviderResult()
            {
                ProviderId = "123",
                SpecificationId = specificationId,
                FundingStreamResult = new PublishedFundingStreamResult()
                {
                    AllocationLineResult = new PublishedAllocationLineResult()
                    {
                        AllocationLine = allocationLine1,
                        Current = new PublishedAllocationLineResultVersion()
                        {
                            Value = 51,
                            Status = AllocationLineStatus.Approved,
                            Provider = new ProviderSummary()
                            {
                                Name = "Provider Name",
                                Id = "123",
                            }
                        }
                    },
                    FundingStream = new FundingStream()
                    {
                        Id = "fsId",
                        Name = "Funding Stream Name",
                        PeriodType = new PeriodType()
                        {
                            Name = "Test Period Type",
                            Id = "tpt",
                        }
                    },
                    FundingStreamPeriod = "fsp",
                },
                FundingPeriod = new Period()
                {
                    Id = "fundingPeriodId",
                    Name = "Funding Period Name"
                },
            };


            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));
            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);
            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();
            assembler
                .AssemblePublishedProviderResults(Arg.Any<IEnumerable<ProviderResult>>(), Arg.Any<Reference>(), Arg.Any<SpecificationCurrentVersion>())
                .Returns(publishedProviderResults);

            assembler
                .GeneratePublishedProviderResultsToSave(Arg.Any<IEnumerable<PublishedProviderResult>>(), Arg.Any<IEnumerable<PublishedProviderResultExisting>>())
                .Returns((publishedProviderResults, existingToRemove));

            publishedProviderResultsRepository
                .GetPublishedProviderResultForId("c3BlYy0xMTIzQUFBQUE=", "123")
                .Returns(existingProviderResultToRemove);

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsAssemblerService: assembler,
                publishedProviderResultsVersionRepository: versionRepository);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            await resultsService.PublishProviderResults(message);

            //Assert

            decimal? expectedValue = 0;

            await
                publishedProviderResultsRepository
                    .Received(1)
                    .SavePublishedResults(Arg.Is<IEnumerable<PublishedProviderResult>>(a =>
                    a.First().ProviderId == "123" &&
                    a.First().FundingStreamResult.AllocationLineResult.Current.Value == expectedValue &&
                    a.First().FundingStreamResult.AllocationLineResult.Current.Status == AllocationLineStatus.Updated));

            await
                publishedProviderResultsRepository
                    .Received(1)
                    .SavePublishedResults(Arg.Is<IEnumerable<PublishedProviderResult>>(a => a.Count() == 1));

        }

        [TestMethod]
        public void PublishProviderResults_WhenFailsToUpdateSpecificationRefreshDate_ThenNoExceptionThrown()
        {
            // Arrange
            string specificationId = "1";

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));

            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);
            specificationsRepository.UpdatePublishedRefreshedDate(Arg.Is(specificationId), Arg.Any<DateTimeOffset>())
                .Returns(Task.FromResult(HttpStatusCode.InternalServerError));

            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();

            ILogger logger = CreateLogger();

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsVersionRepository: versionRepository,
                logger: logger,
                publishedProviderResultsAssemblerService: assembler);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;

            // Act
            Func<Task> test = () => resultsService.PublishProviderResults(message);

            //Assert
            test.Should().NotThrow();
            logger.Received(1).Error(Arg.Is($"Failed to update the published refresh date on the specification with id: {specificationId}. Failed with code: InternalServerError"));
            logger.DidNotReceive().Information(Arg.Is($"Updated the published refresh date on the specification with id: {specificationId}"));
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenUseJobServiceToggleSet_ThenJobServiceCalled()
        {
            // Arrange
            string specificationId = "1";

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));

            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);

            specificationsRepository.UpdatePublishedRefreshedDate(Arg.Is(specificationId), Arg.Any<DateTimeOffset>())
                .Returns(Task.FromResult(HttpStatusCode.OK));

            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);

            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();
           
            ILogger logger = CreateLogger();

            IFeatureToggle featureToggle = CreateFeatureToggle();
            featureToggle
                .IsJobServiceForPublishProviderResultsEnabled()
                .Returns(true);

            IJobsApiClient jobsApiClient = CreateJobsApiClient();
            jobsApiClient
                .GetJobById(Arg.Is(jobId))
                .Returns(new ApiResponse<JobViewModel>(HttpStatusCode.OK, new JobViewModel { Id = jobId }));
            jobsApiClient
                .AddJobLog(Arg.Is(jobId), Arg.Any<JobLogUpdateModel>())
                .Returns(new ApiResponse<JobLog>(HttpStatusCode.OK, new JobLog()));

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsVersionRepository: versionRepository,
                publishedProviderResultsAssemblerService: assembler,
                logger: logger,
                featureToggle: featureToggle,
                jobsApiClient: jobsApiClient);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;
            message.UserProperties["jobId"] = jobId;

            // Act
            await resultsService.PublishProviderResults(message);

            //Assert
            logger.DidNotReceive().Error(Arg.Any<string>());
            logger.Received(1).Information(Arg.Is($"Updated the published refresh date on the specification with id: {specificationId}"));

            await jobsApiClient.Received(1).AddJobLog(Arg.Is(jobId), Arg.Is<JobLogUpdateModel>(l => l.CompletedSuccessfully.HasValue == false && l.ItemsProcessed == 0));
            await jobsApiClient.Received(1).AddJobLog(Arg.Is(jobId), Arg.Is<JobLogUpdateModel>(l => l.CompletedSuccessfully.HasValue == false && l.ItemsProcessed == 5));
            await jobsApiClient.Received(1).AddJobLog(Arg.Is(jobId), Arg.Is<JobLogUpdateModel>(l => l.CompletedSuccessfully.HasValue == false && l.ItemsProcessed == 10));
            await jobsApiClient.Received(1).AddJobLog(Arg.Is(jobId), Arg.Is<JobLogUpdateModel>(l => l.CompletedSuccessfully.HasValue == false && l.ItemsProcessed == 28));
            await jobsApiClient.Received(1).AddJobLog(Arg.Is(jobId), Arg.Is<JobLogUpdateModel>(l => l.CompletedSuccessfully.HasValue == false && l.ItemsProcessed == 33));
        }

        [TestMethod]
        public async Task PublishProviderResults_WhenUseJobServiceToggleSet_ThenFetchProfilePeriodIsUsingJobService()
        {
            // Arrange
            string specificationId = "1";

            SpecificationCurrentVersion specificationCurrentVersion = new SpecificationCurrentVersion
            {
                Id = specificationId
            };

            IEnumerable<ProviderResult> providerResults = new List<ProviderResult>
            {
                new ProviderResult()
            };

            List<PublishedProviderResult> publishedProviderResults = new List<PublishedProviderResult>();

            for (int i = 0; i < 560; i++)
            {
                publishedProviderResults
                    .Add(new PublishedProviderResult()
                    {
                        FundingStreamResult = new PublishedFundingStreamResult()
                        {
                            AllocationLineResult = new PublishedAllocationLineResult()
                            {
                                Current = new PublishedAllocationLineResultVersion()
                                {
                                    Status = AllocationLineStatus.Updated
                                },
                                AllocationLine = new AllocationLine()
                                {
                                    Id = "alId",
                                    Name = "Allocation Line",
                                },
                            }
                        }
                    });
            }

            ICalculationResultsRepository resultsRepository = CreateResultsRepository();
            resultsRepository.GetProviderResultsBySpecificationId(Arg.Is(specificationId), Arg.Is(-1))
                .Returns(Task.FromResult(providerResults));

            ISpecificationsRepository specificationsRepository = CreateSpecificationsRepository();
            specificationsRepository.GetCurrentSpecificationById(Arg.Is(specificationId))
                .Returns(specificationCurrentVersion);

            specificationsRepository.UpdatePublishedRefreshedDate(Arg.Is(specificationId), Arg.Any<DateTimeOffset>())
                .Returns(Task.FromResult(HttpStatusCode.OK));

            IPublishedProviderResultsRepository publishedProviderResultsRepository = CreatePublishedProviderResultsRepository();
            publishedProviderResultsRepository.SavePublishedResults(Arg.Any<IEnumerable<PublishedProviderResult>>())
                .Returns(Task.CompletedTask);

            IVersionRepository<PublishedAllocationLineResultVersion> versionRepository = CreatePublishedProviderResultsVersionRepository();
            versionRepository.SaveVersions(Arg.Any<IEnumerable<PublishedAllocationLineResultVersion>>())
                .Returns(Task.CompletedTask);


            IPublishedProviderResultsAssemblerService assembler = CreateResultsAssembler();
            assembler
                .AssemblePublishedProviderResults(Arg.Is(providerResults), Arg.Any<Reference>(), Arg.Is(specificationCurrentVersion))
                .Returns(publishedProviderResults);

            ILogger logger = CreateLogger();

            IFeatureToggle featureToggle = CreateFeatureToggle();
            featureToggle
                .IsJobServiceForPublishProviderResultsEnabled()
                .Returns(true);

            IJobsApiClient jobsApiClient = CreateJobsApiClient();
            jobsApiClient
                .GetJobById(Arg.Is(jobId))
                .Returns(new ApiResponse<JobViewModel>(HttpStatusCode.OK, new JobViewModel { Id = jobId }));
            jobsApiClient
                .AddJobLog(Arg.Is(jobId), Arg.Any<JobLogUpdateModel>())
                .Returns(new ApiResponse<JobLog>(HttpStatusCode.OK, new JobLog()));
            jobsApiClient
                .CreateJob(Arg.Is<JobCreateModel>(j => j.JobDefinitionId == JobConstants.DefinitionNames.FetchProviderProfileJob))
                .Returns(new Job { Id = "fpp-job" });

            PublishedResultsService resultsService = CreateResultsService(resultsRepository: resultsRepository,
                publishedProviderResultsRepository: publishedProviderResultsRepository,
                specificationsRepository: specificationsRepository,
                publishedProviderResultsVersionRepository: versionRepository,
                publishedProviderResultsAssemblerService: assembler,
                logger: logger,
                featureToggle: featureToggle,
                jobsApiClient: jobsApiClient);

            Message message = new Message();
            message.UserProperties["specification-id"] = specificationId;
            message.UserProperties["jobId"] = jobId;

            // Act
            await resultsService.PublishProviderResults(message);

            //Assert
            logger.DidNotReceive().Error(Arg.Any<string>());
            logger.Received(1).Information(Arg.Is($"Updated the published refresh date on the specification with id: {specificationId}"));

            await jobsApiClient.Received(6).CreateJob(Arg.Is<JobCreateModel>(j => j.JobDefinitionId == JobConstants.DefinitionNames.FetchProviderProfileJob && j.SpecificationId == specificationId && j.ParentJobId == jobId));
        }

        private static SpecificationCalculationExecutionStatus CreateSpecificationCalculationProgress(Action<SpecificationCalculationExecutionStatus> defaultModelAction)
        {
            SpecificationCalculationExecutionStatus defaultModel = new SpecificationCalculationExecutionStatus(SpecificationId1, 0, CalculationProgressStatus.InProgress);
            defaultModelAction(defaultModel);
            return defaultModel;
        }
    }
}
