﻿using CalculateFunding.Models;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Exceptions;
using CalculateFunding.Models.Versioning;
using CalculateFunding.Repositories.Common.Search;
using CalculateFunding.Services.Calcs.Interfaces;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using NSubstitute;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CalculateFunding.Services.Core.Interfaces.ServiceBus;
using Microsoft.Azure.ServiceBus;
using CalculateFunding.Services.Core.Constants;

namespace CalculateFunding.Services.Calcs.Services
{
    public partial class CalculationServiceTests
    {
        [TestMethod]
        public void UpdateCalulationsForSpecification_GivenInvalidModel_LogsDoesNotSave()
        {
            //Arrange
            dynamic anyObject = new { something = 1 };

            string json = JsonConvert.SerializeObject(anyObject);

            Message message = new Message(Encoding.UTF8.GetBytes(json));

            CalculationService service = CreateCalculationService();

            //Act
            Func<Task> test = async () => await service.UpdateCalculationsForSpecification(message);

            //Assert
            test
              .ShouldThrowExactly<InvalidModelException>();
        }

        [TestMethod]
        public async Task UpdateCalulationsForSpecification_GivenModelHasNoChanges_LogsAndReturns()
        {
            //Arrange
            Models.Specs.SpecificationVersionComparisonModel specificationVersionComparison = new Models.Specs.SpecificationVersionComparisonModel()
            {
                Current = new Models.Specs.SpecificationVersion { FundingPeriod = new Reference { Id = "fp1" } },
                Previous = new Models.Specs.SpecificationVersion { FundingPeriod = new Reference { Id = "fp1" } }
            };

            string json = JsonConvert.SerializeObject(specificationVersionComparison);

            Message message = new Message(Encoding.UTF8.GetBytes(json));

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            await service.UpdateCalculationsForSpecification(message);

            //Assert
            logger
                .Received(1)
                .Information(Arg.Is("No changes detected"));
        }

        [TestMethod]
        public async Task UpdateCalulationsForSpecification_GivenModelHasChangedFundingPeriodsButCalcculationsCouldNotBeFound_LogsAndReturns()
        {
            //Arrange
            const string specificationId = "spec-id";

            Models.Specs.SpecificationVersionComparisonModel specificationVersionComparison = new Models.Specs.SpecificationVersionComparisonModel()
            {
                Id = specificationId,
                Current = new Models.Specs.SpecificationVersion { FundingPeriod = new Reference { Id = "fp2" } },
                Previous = new Models.Specs.SpecificationVersion { FundingPeriod = new Reference { Id = "fp1" } }
            };

            string json = JsonConvert.SerializeObject(specificationVersionComparison);

            Message message = new Message(Encoding.UTF8.GetBytes(json));

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationsBySpecificationId(Arg.Is(specificationId))
                .Returns((IEnumerable<Calculation>)null);

            CalculationService service = CreateCalculationService(calculationsRepository, logger);

            //Act
            await service.UpdateCalculationsForSpecification(message);

            //Assert
            logger
                .Received(1)
                .Information(Arg.Is($"No calculations found for specification id: {specificationId}"));
        }

        [TestMethod]
        public async Task UpdateCalulationsForSpecification_GivenModelHasChangedFundingPeriodsButBuildProjectNotFound_EnsuresCreatesBuildProject()
        {
            //Arrange
            const string specificationId = "spec-id";

            Models.Specs.SpecificationVersionComparisonModel specificationVersionComparison = new Models.Specs.SpecificationVersionComparisonModel()
            {
                Id = specificationId,
                Current = new Models.Specs.SpecificationVersion
                {
                    FundingPeriod = new Reference { Id = "fp2" },
                    Name = "any-name"
                },
                Previous = new Models.Specs.SpecificationVersion { FundingPeriod = new Reference { Id = "fp1" } }
            };

            string json = JsonConvert.SerializeObject(specificationVersionComparison);

            Message message = new Message(Encoding.UTF8.GetBytes(json));

            ILogger logger = CreateLogger();

            IEnumerable<Calculation> calcs = new[]
            {
                new Calculation
                {
                    SpecificationId =  "spec-id",
                    Name = "any name",
                    Id = "any-id",
                    CalculationSpecification = new Reference("any name", "any-id"),
                    FundingPeriod = new Reference("18/19", "2018/2019"),
                    CalculationType = CalculationType.Number,
                    FundingStream = new Reference("fs1","fs1-111"),
                    Current = new CalculationVersion
                    {
                        Author = new Reference(UserId, Username),
                        Date = DateTime.UtcNow,
                        PublishStatus = PublishStatus.Draft,
                        SourceCode = "source code",
                        Version = 1
                    },
                    Policies = new List<Reference>()
                }
            };

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationsBySpecificationId(Arg.Is(specificationId))
                .Returns(calcs);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();

            CalculationService service = CreateCalculationService(calculationsRepository, logger, buildProjectsRepository: buildProjectsRepository);

            //Act
            await service.UpdateCalculationsForSpecification(message);

            //Assert
            await
                buildProjectsRepository
                    .Received(1)
                    .CreateBuildProject(Arg.Any<BuildProject>());

            logger
                .Received(1)
                .Warning(Arg.Is($"A build project could not be found for specification id: {specificationId}"));
        }

        [TestMethod]
        public async Task UpdateCalculationsForSpecification_GivenModelHasChangedFundingPeriodsButBuildProjectNotFound_AssignsCalculationsToBuildProjectAndSaves()
        {
            // Arrange
            const string specificationId = "spec-id";

            Models.Specs.SpecificationVersionComparisonModel specificationVersionComparison = new Models.Specs.SpecificationVersionComparisonModel()
            {
                Id = specificationId,
                Current = new Models.Specs.SpecificationVersion
                {
                    FundingPeriod = new Reference { Id = "fp2" },
                    Name = "any-name"
                },
                Previous = new Models.Specs.SpecificationVersion { FundingPeriod = new Reference { Id = "fp1" } }
            };

            string json = JsonConvert.SerializeObject(specificationVersionComparison);

            Message message = new Message(Encoding.UTF8.GetBytes(json));

            ILogger logger = CreateLogger();

            IEnumerable<Calculation> calcs = new[]
            {
                new Calculation
                {
                    SpecificationId =  "spec-id",
                    Name = "any name",
                    Id = "any-id",
                    CalculationSpecification = new Reference("any name", "any-id"),
                    FundingPeriod = new Reference("18/19", "2018/2019"),
                    CalculationType = CalculationType.Number,
                    FundingStream = new Reference("fs1","fs1-111"),
                    Current = new CalculationVersion
                    {
                        Author = new Reference(UserId, Username),
                        Date = DateTime.UtcNow,
                        PublishStatus = PublishStatus.Draft,
                        SourceCode = "source code",
                        Version = 1
                    },
                    Policies = new List<Reference>()
                }
            };

            BuildProject buildProject = null;

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationsBySpecificationId(Arg.Is(specificationId))
                .Returns(calcs);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectBySpecificationId(Arg.Is(specificationId))
                .Returns(buildProject);

            IMessengerService messengerService = CreateMessengerService();

            CalculationService service = CreateCalculationService(calculationsRepository, logger, buildProjectsRepository: buildProjectsRepository, messengerService: messengerService);

            // Act
            await service.UpdateCalculationsForSpecification(message);

            // Assert
            calcs
                .First()
                .FundingPeriod.Id
                .Should()
                .Be("fp2");

            await buildProjectsRepository
               .Received(1)
               .CreateBuildProject(Arg.Any<BuildProject>());

            await
                messengerService
                    .Received(1)
                    .SendToQueue(Arg.Is(ServiceBusConstants.QueueNames.CalculationJobInitialiser), Arg.Any<BuildProject>(), Arg.Any<IDictionary<string, string>>());
        }

        [TestMethod]
        public async Task UpdateCalculationsForSpecification_GivenModelHasChangedPolicyName_SavesChanges()
        {
            // Arrange
            const string specificationId = "spec-id";

            Models.Specs.SpecificationVersionComparisonModel specificationVersionComparison = new Models.Specs.SpecificationVersionComparisonModel()
            {
                Id = specificationId,
                Current = new Models.Specs.SpecificationVersion
                {
                    FundingPeriod = new Reference { Id = "fp1" },
                    Name = "any-name",
                    Policies = new[] { new Models.Specs.Policy { Id = "pol-id", Name = "policy2" } }
                },
                Previous = new Models.Specs.SpecificationVersion
                {
                    FundingPeriod = new Reference { Id = "fp1" },
                    Policies = new[] { new Models.Specs.Policy { Id = "pol-id", Name = "policy1" } }
                }
            };

            string json = JsonConvert.SerializeObject(specificationVersionComparison);

            Message message = new Message(Encoding.UTF8.GetBytes(json));

            ILogger logger = CreateLogger();

            IEnumerable<Calculation> calcs = new[]
            {
                new Calculation
                {
                    SpecificationId =  "spec-id",
                    Name = "any name",
                    Id = "any-id",
                    CalculationSpecification = new Reference("any name", "any-id"),
                    FundingPeriod = new Reference("18/19", "2018/2019"),
                    CalculationType = CalculationType.Number,
                    FundingStream = new Reference("fp1","fs1-111"),
                    Current = new CalculationVersion
                    {
                        Author = new Reference(UserId, Username),
                        Date = DateTime.UtcNow,
                        PublishStatus = PublishStatus.Draft,
                        SourceCode = "source code",
                        Version = 1
                    },
                    Policies = new List<Reference>{ new Reference { Id = "pol-id", Name = "policy1"} }
                }
            };

            BuildProject buildProject = null;

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationsBySpecificationId(Arg.Is(specificationId))
                .Returns(calcs);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectBySpecificationId(Arg.Is(specificationId))
                .Returns(buildProject);

            IMessengerService messengerService = CreateMessengerService();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();

            CalculationService service = CreateCalculationService(calculationsRepository, logger, buildProjectsRepository: buildProjectsRepository, searchRepository: searchRepository);

            // Act
            await service.UpdateCalculationsForSpecification(message);

            // Assert
            calcs
                .First()
                .Policies
                .First()
                .Name
                .Should()
                .Be("policy2");

            await
                searchRepository
                    .Received(1)
                    .Index(Arg.Is<IEnumerable<CalculationIndex>>(m => m.First().PolicySpecificationNames.Contains("policy2")));
        }

        [TestMethod]
        public async Task UpdateCalculationsForSpecification_GivenModelHasChangedFundingStreams_SetsTheAllocationLineAndFundingStreamToNull()
        {
            //Arrange
            const string specificationId = "spec-id";

            Models.Specs.SpecificationVersionComparisonModel specificationVersionComparison = new Models.Specs.SpecificationVersionComparisonModel()
            {
                Id = specificationId,
                Current = new Models.Specs.SpecificationVersion
                {
                    FundingPeriod = new Reference { Id = "fp1" },
                    Name = "any-name",
                    FundingStreams = new List<Reference> { new Reference { Id = "fs2" } }
                },
                Previous = new Models.Specs.SpecificationVersion
                {
                    FundingPeriod = new Reference { Id = "fp1" },
                    FundingStreams = new List<Reference> { new Reference { Id = "fs1" } }
                }
            };

            string json = JsonConvert.SerializeObject(specificationVersionComparison);

            Message message = new Message(Encoding.UTF8.GetBytes(json));

            ILogger logger = CreateLogger();

            IEnumerable<Calculation> calcs = new[]
            {
                new Calculation
                {
                    SpecificationId =  "spec-id",
                    Name = "any name",
                    Id = "any-id",
                    CalculationSpecification = new Reference("any name", "any-id"),
                    FundingPeriod = new Reference("18/19", "2018/2019"),
                    CalculationType = CalculationType.Number,
                    FundingStream = new Reference("fs1","fs1-111"),
                    Current = new CalculationVersion
                    {
                        Author = new Reference(UserId, Username),
                        Date = DateTime.UtcNow,
                        PublishStatus = PublishStatus.Draft,
                        SourceCode = "source code",
                        Version = 1
                    },
                    Policies = new List<Reference>()
                }
            };

            BuildProject buildProject = new BuildProject();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationsBySpecificationId(Arg.Is(specificationId))
                .Returns(calcs);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectBySpecificationId(Arg.Is(specificationId))
                .Returns(buildProject);

            IMessengerService messengerService = CreateMessengerService();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();

            CalculationService service = CreateCalculationService(calculationsRepository, logger, buildProjectsRepository: buildProjectsRepository, messengerService: messengerService, searchRepository: searchRepository);

            //Act
            await service.UpdateCalculationsForSpecification(message);

            //Assert
            calcs
                .First()
                .FundingStream
                .Should()
                .BeNull();

            calcs
               .First()
               .AllocationLine
               .Should()
               .BeNull();

            await searchRepository
                .Received(1)
                .Index(Arg.Is<IEnumerable<CalculationIndex>>(c =>
                    c.First().Id == calcs.First().Id &&
                    c.First().FundingStreamId == "" &&
                    c.First().FundingStreamName == "No funding stream set"));
            await
                messengerService
                    .Received(1)
                    .SendToQueue(Arg.Is(ServiceBusConstants.QueueNames.CalculationJobInitialiser), Arg.Is(buildProject), Arg.Any<IDictionary<string, string>>());
        }
    }
}
