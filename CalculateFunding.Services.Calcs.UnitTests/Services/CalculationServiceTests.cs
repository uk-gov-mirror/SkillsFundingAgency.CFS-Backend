﻿using CalculateFunding.Models;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Exceptions;
using CalculateFunding.Models.Versioning;
using CalculateFunding.Repositories.Common.Search;
using CalculateFunding.Services.Calcs.Interfaces;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Search.Models;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using NSubstitute;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CalculateFunding.Services.Calcs.Interfaces.CodeGen;
using CalculateFunding.Services.Compiler.Interfaces;
using CalculateFunding.Services.Compiler;

namespace CalculateFunding.Services.Calcs.Services
{
    [TestClass]
    public class CalculationServiceTests
    {
        const string UserId = "8bcd2782-e8cb-4643-8803-951d715fc202";
        const string CalculationId = "3abc2782-e8cb-4643-8803-951d715fci23";
        const string Username = "test-user";

        [TestMethod]
        public async Task CreateCalculation_GivenNullCalculation_LogsDoesNotSave()
        {
            //Arrange
            Message message = new Message();

            ICalculationsRepository repository = CreateCalculationsRepository();

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(repository, logger);

            //Act
            await service.CreateCalculation(message);

            //Assert
            logger
                .Received(1)
                .Error("A null calculation was provided to CalculateFunding.Services.Calcs.CreateCalculation");

            await
                repository
                    .DidNotReceive()
                    .CreateDraftCalculation(Arg.Any<Calculation>());
        }

        [TestMethod]
        public void CreateCalculation_GivenInvalidCalculation_LogsDoesNotSave()
        {
            //Arrange
            Message message = new Message();

            dynamic anyObject = new { something = 1 };

            string json = JsonConvert.SerializeObject(anyObject);

            message.Body = Encoding.UTF8.GetBytes(json);

            ICalculationsRepository repository = CreateCalculationsRepository();

            ILogger logger = CreateLogger();

            ValidationResult validationResult = new ValidationResult(new[]{
                    new ValidationFailure("prop1", "any error")
                });

            IValidator<Calculation> validator = CreateCalculationValidator(validationResult);

            CalculationService service = CreateCalculationService(repository, logger, calcValidator: validator);

            //Act
            Func<Task> test = async () => await service.CreateCalculation(message);

            //Assert
            test
              .ShouldThrowExactly<InvalidModelException>();
        }

        [TestMethod]
        public async Task CreateCalculation_GivenValidCalculation_ButFailedToSave_DoesNotUpdateSearch()
        {
            //Arrange
            Message message = new Message();

            Calculation calculation = new Calculation { Id = CalculationId };

            string json = JsonConvert.SerializeObject(calculation);

            message.Body = Encoding.UTF8.GetBytes(json);

            message.UserProperties.Add("user-id", UserId);
            message.UserProperties.Add("user-name", Username);

            ICalculationsRepository repository = CreateCalculationsRepository();
            repository
                .CreateDraftCalculation(Arg.Any<Calculation>())
                .Returns(HttpStatusCode.BadRequest);

            ILogger logger = CreateLogger();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();

            CalculationService service = CreateCalculationService(repository, logger, searchRepository);

            //Act
            await service.CreateCalculation(message);

            //Assert
            logger
                .Received(1)
                .Error($"There was problem creating a new calculation with id {calculation.Id} in Cosmos Db with status code 400");

            await
               repository
                   .Received(1)
                   .CreateDraftCalculation(Arg.Is<Calculation>( m =>
                        m.Id == CalculationId &&
                        m.Current.PublishStatus == PublishStatus.Draft &&
                        m.Current.Author.Id == UserId &&
                        m.Current.Author.Name == Username &&
                        m.Current.Date.Date == DateTime.UtcNow.Date &&
                        m.Current.Version == 1 &&
                        m.Current.DecimalPlaces == 6
                   ));

            await
                searchRepository
                    .DidNotReceive()
                    .Index(Arg.Any<List<CalculationIndex>>());
        }

        [TestMethod]
        public async Task CreateCalculation_GivenValidCalculation_AndSavesLogs()
        {
            //Arrange
            Message message = new Message();

            Calculation calculation = CreateCalculation();

            IEnumerable<Calculation> calculations = new[]
            {
                calculation
            };

            string json = JsonConvert.SerializeObject(calculation);

            message.Body = Encoding.UTF8.GetBytes(json);

            message.UserProperties.Add("user-id", UserId);
            message.UserProperties.Add("user-name", Username);

            ICalculationsRepository repository = CreateCalculationsRepository();
            repository
                .CreateDraftCalculation(Arg.Any<Calculation>())
                .Returns(HttpStatusCode.Created);
            repository
                .GetCalculationsBySpecificationId(Arg.Is("any-spec-id"))
                .Returns(calculations);

            ILogger logger = CreateLogger();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();

            CalculationService service = CreateCalculationService(repository, logger, searchRepository);

            //Act
            await service.CreateCalculation(message);

            //Assert
            logger
                .Received(1)
                .Information($"Calculation with id: {calculation.Id} was succesfully saved to Cosmos Db");

            await
               repository
                   .Received(1)
                   .CreateDraftCalculation(Arg.Is<Calculation>(m =>
                       m.Id == CalculationId &&
                       m.Current.PublishStatus == PublishStatus.Draft &&
                       m.Current.Author.Id == UserId &&
                       m.Current.Author.Name == Username &&
                       m.Current.Date.Date == DateTime.UtcNow.Date &&
                       m.Current.Version == 1 &&
                       m.Current.DecimalPlaces == 6 &&
                       m.History.First().PublishStatus == PublishStatus.Draft &&
                       m.History.First().Author.Id == UserId &&
                       m.History.First().Author.Name == Username &&
                       m.History.First().Date.Date == DateTime.UtcNow.Date &&
                       m.History.First().Version == 1 &&
                       m.History.First().DecimalPlaces == 6
                   ));

            await
                searchRepository
                    .Received(1)
                    .Index(Arg.Is<List<CalculationIndex>>(
                        m => m.First().Id == CalculationId &&
                        m.First().Name == "Test Calc Name" &&
                        m.First().CalculationSpecificationId == "any-calc-id" &&
                        m.First().CalculationSpecificationName == "Test Calc Name" &&
                        m.First().SpecificationId == "any-spec-id" &&
                        m.First().SpecificationName == "Test Spec Name" &&
                        m.First().PeriodId == "18/19" &&
                        m.First().PeriodName == "2018/2019" &&
                        m.First().AllocationLineId == "test-alloc-id" &&
                        m.First().AllocationLineName == "test-alloc-name" &&
                        m.First().PolicySpecificationIds.First() == "policy-id" &&
                        m.First().PolicySpecificationNames.First() == "policy-name"
                  ));
        }

        [TestMethod]
        async public Task GetCalculationById_GivenNoCalculationIdWasprovided_ReturnsBadRequest()
        {
            //Arrange
            HttpRequest request = Substitute.For<HttpRequest>();

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            IActionResult result = await service.GetCalculationById(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is("No calculation Id was provided to GetCalculationById"));
        }

        [TestMethod]
        async public Task GetCalculationById_GivenCalculationIdWasProvidedButCalculationCouldNotBeFound_ReturnsNotFound()
        {
            //Arrange
            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns((Calculation)null);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.GetCalculationById(request);

            //Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"A calculation was not found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task GetCalculationById_GivenCalculationIdWasProvidedAndcalculationWasfound_ReturnsOK()
        {
            //Arrange
            Calculation calculation = new Calculation();

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.GetCalculationById(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"A calculation was found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task GetCalculationHistory_GivenNoCalculationIdWasProvided_ReturnsBadRequest()
        {
            //Arrange
            HttpRequest request = Substitute.For<HttpRequest>();

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            IActionResult result = await service.GetCalculationHistory(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is("No calculation Id was provided to GetCalculationHistory"));
        }

        [TestMethod]
        async public Task GetCalculationHistory_GivenCalculationIdWasProvidedButHistoryWasNull_ReturnsNotFound()
        {
            //Arrange
            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }
            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetVersionHistory(Arg.Is(CalculationId))
                .Returns((IEnumerable<CalculationVersion>)null);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.GetCalculationHistory(request);

            //Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"A calculation was not found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task GetCalculationHistory_GivenCalculationIdWasProvided_ReturnsOK()
        {
            //Arrange
            IEnumerable<CalculationVersion> versions = new List<CalculationVersion>
            {
                new CalculationVersion(),
                new CalculationVersion()
            };

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }
            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetVersionHistory(Arg.Is(CalculationId))
                .Returns(versions);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.GetCalculationHistory(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();
        }

        [TestMethod]
        public async Task GetCalculationCurrentVersion_GivenNoCalculationIdprovided_returnsBadrequest()
        {
            //Arrange
            HttpRequest request = Substitute.For<HttpRequest>();

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            IActionResult result = await service.GetCalculationCurrentVersion(request);

            //Assert
            logger
                .Received(1)
                .Error("No calculation Id was provided to GetCalculationCurrentVersion");

            result
                 .Should()
                 .BeOfType<BadRequestObjectResult>();
        }

        [TestMethod]
        async public Task GetCalculationCurrentVersion_GivencalculationWasNotFound_ReturnsNotFound()
        {
            //Arrange
            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }
            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns((Calculation)null);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.GetCalculationCurrentVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"A calculation was not found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task GetCalculationCurrentVersion_GivencalculationWasFoundWithNoCurrent_ReturnsNotFound()
        {
            //Arrange
            Calculation calculation = new Calculation();

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }
            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.GetCalculationCurrentVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"A current calculation was not found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task GetCalculationCurrentVersion_GivencalculationWasFound_ReturnsOK()
        {
            //Arrange
            Calculation calculation = new Calculation
            {
                Specification = new Models.Results.SpecificationSummary
                {
                    Id = "spec-id"
                },
                Current = new CalculationVersion
                {
                    Author = new Reference(UserId, Username),
                    Date = DateTime.UtcNow,
                    PublishStatus = PublishStatus.Draft,
                    SourceCode = "source code",
                    Version = 1
                },
                Name = "any name",
                Id = "any-id",
                CalculationSpecification = new Reference("any name", "any-id"),
                Period = new Reference("18/19", "2018/2019")
            };

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }
            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.GetCalculationCurrentVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenNoCalculationIdWasProvided_ReturnsBadRequest()
        {
            //Arrange
            HttpRequest request = Substitute.For<HttpRequest>();

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is("No calculation Id was provided to GetCalculationHistory"));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationIdButNoModelSupplied_ReturnsBadRequest()
        {
            //Arrange
            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is($"Null or empty source code was provided for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenModelDoesNotContainSourceCiode_ReturnsBadRequest()
        {
            //Arrange
            SaveSourceCodeVersion model = new SaveSourceCodeVersion();
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is($"Null or empty source code was provided for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationExistsWithNoHistory_CreatesNewVersion()
        {
            //Arrange
            Calculation calculation = CreateCalculation();

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository, 
                buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"History for {CalculationId} was null or empty and needed recreating."));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenModelButCalculationDoesNotExist_ReturnsNotFound()
        {
            //Arrange
            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns((Calculation)null);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Error(Arg.Is($"A calculation was not found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationExistsWithNoCurrent_CreatesNewVersion()
        {
            //Arrange
            Calculation calculation = CreateCalculation();
            calculation.Current = null;

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(logger: logger, 
                calculationsRepository: calculationsRepository, 
                buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Warning(Arg.Is($"Current for {CalculationId} was null and needed recreating."));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationExistsWithNoBuildId_CreatesNewBuildProject()
        {
            //Arrange
            Calculation calculation = CreateCalculation();

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(logger: logger, 
                calculationsRepository: calculationsRepository, 
                buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Warning(Arg.Is($"Build project for specification {calculation.Specification.Id} could not be found, creating a new one"));

            await
                buildProjectsRepository
                    .Received(1)
                    .CreateBuildProject(Arg.Any<BuildProject>());
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationExistsWithButBuildProjectDoesNotExist_CreatesNewBuildProject()
        {
            //Arrange
            string buildProjectId = Guid.NewGuid().ToString();

            Calculation calculation = CreateCalculation();
            calculation.BuildProjectId = buildProjectId;

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(logger: logger, 
                calculationsRepository: calculationsRepository, 
                buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Warning(Arg.Is($"Build project for specification {calculation.Specification.Id} could not be found, creating a new one"));

            await
                buildProjectsRepository
                    .Received(1)
                    .CreateBuildProject(Arg.Any<BuildProject>());
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationExistsWithBuildIdButNoCalculations_EnsuresCalculationsCreatedUpdatesBuildProject()
        {
            //Arrange
            string buildProjectId = Guid.NewGuid().ToString();

            BuildProject buildProject = new BuildProject
            {
                Id = buildProjectId
            };

            Calculation calculation = CreateCalculation();
            calculation.BuildProjectId = buildProjectId;

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectById(Arg.Is(buildProjectId))
                .Returns(buildProject);

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(
                logger: logger, calculationsRepository: calculationsRepository,
                buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Warning(Arg.Is($"Build project for specification {calculation.Specification.Id} could not be found, creating a new one"));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationExistsWithBuildIdButCalculationCouldNotBeFound_AddsCalculationUpdatesBuildProject()
        {
            //Arrange
            string buildProjectId = Guid.NewGuid().ToString();

            BuildProject buildProject = new BuildProject
            {
                Id = buildProjectId,
                Calculations = new List<Calculation>()
            };

            Calculation calculation = CreateCalculation();
            calculation.BuildProjectId = buildProjectId;

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectById(Arg.Is(buildProjectId))
                .Returns(buildProject);

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(logger: logger, 
                calculationsRepository: calculationsRepository, 
               buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Warning(Arg.Is($"Build project for specification {calculation.Specification.Id} could not be found, creating a new one"));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationExistsWithBuildIdButButNotInSearch_CreatesSearchDocument()
        {
            //Arrange
            string buildProjectId = Guid.NewGuid().ToString();

            BuildProject buildProject = new BuildProject
            {
                Id = buildProjectId,
                Calculations = new List<Calculation>()
            };

            Calculation calculation = CreateCalculation();

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectById(Arg.Is(buildProjectId))
                .Returns(buildProject);

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns((CalculationIndex)null);

            CalculationService service = CreateCalculationService(logger: logger,
                calculationsRepository: calculationsRepository,
               buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            await
                searchRepository
                    .Received(1)
                    .Index(Arg.Any<IList<CalculationIndex>>());
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationIsCurrentlyPublished_SetsPublishStateToUpdated()
        {
            //Arrange
            string buildProjectId = Guid.NewGuid().ToString();

            BuildProject buildProject = new BuildProject
            {
                Id = buildProjectId,
                Calculations = new List<Calculation>()
            };

            Calculation calculation = CreateCalculation();
            calculation.Current.PublishStatus = PublishStatus.Published;

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectById(Arg.Is(buildProjectId))
                .Returns(buildProject);

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(logger: logger,
                calculationsRepository: calculationsRepository,
               buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            calculation
                .Current
                .PublishStatus
                .Should()
                .Be(PublishStatus.Updated);

            await
                searchRepository
                .Received(1)
                .Index(Arg.Is<IList<CalculationIndex>>(m => m.First().Status == "Updated"));
        }

        [TestMethod]
        async public Task SaveCalculationVersion_GivenCalculationIsCurrentlyUpdated_SetsPublishStateToUpdated()
        {
            //Arrange
            string buildProjectId = Guid.NewGuid().ToString();

            BuildProject buildProject = new BuildProject
            {
                Id = buildProjectId,
                Calculations = new List<Calculation>()
            };

            Calculation calculation = CreateCalculation();
            calculation.BuildProjectId = buildProjectId;
            calculation.Current.PublishStatus = PublishStatus.Updated;

            SaveSourceCodeVersion model = new SaveSourceCodeVersion
            {
                SourceCode = "source code"
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            IBuildProjectsRepository buildProjectsRepository = CreateBuildProjectsRepository();
            buildProjectsRepository
                .GetBuildProjectById(Arg.Is(buildProjectId))
                .Returns(buildProject);

            CalculationIndex calcIndex = new CalculationIndex();

            ISearchRepository<CalculationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .SearchById(Arg.Is(CalculationId))
                .Returns(calcIndex);

            CalculationService service = CreateCalculationService(logger: logger,
                calculationsRepository: calculationsRepository,
               buildProjectsRepository: buildProjectsRepository, serachRepository: searchRepository);

            //Act
            IActionResult result = await service.SaveCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            calculation
                .Current
                .PublishStatus
                .Should()
                .Be(PublishStatus.Updated);

            await
                searchRepository
                .Received(1)
                .Index(Arg.Is<IList<CalculationIndex>>(m => m.First().Status == "Updated"));
        }

        [TestMethod]
        async public Task PublishCalculationVersion_GivenNoCalculationIdWasprovided_ReturnsBadRequest()
        {
            //Arrange
            HttpRequest request = Substitute.For<HttpRequest>();

            ILogger logger = CreateLogger();

            CalculationService service = CreateCalculationService(logger: logger);

            //Act
            IActionResult result = await service.PublishCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is("No calculation Id was provided to PublishCalculationVersion"));
        }

        [TestMethod]
        async public Task PublishCalculationVersion_GivenCalculationIdWasProvidedButCalculationCouldNotBeFound_ReturnsNotFound()
        {
            //Arrange
            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns((Calculation)null);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.PublishCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"A calculation was not found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task PublishCalculationVersion_GivenCalculationIdWasProvidedButCalculationCurrentCouldNotBeFound_ReturnsNotFound()
        {
            //Arrange
            Calculation calculation = new Calculation();

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.PublishCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"A current calculation was not found for calculation id {CalculationId}"));
        }

        [TestMethod]
        async public Task PublishCalculationVersion_GivenCalculationIdWasProvidedButAlreadyPublished_ReturnsOKResulkt()
        {
            //Arrange
            Calculation calculation = new Calculation
            {
                Current = new CalculationVersion
                {
                    PublishStatus = PublishStatus.Published
                }
            };

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }

            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.PublishCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();
        }

        [TestMethod]
        async public Task PublishCalculationVersion_GivenCalculationIsDraftButNoBuildProjectIdFound_PublishesAndCreratesBuildProjectIdReturnsOKResulkt()
        {
            //Arrange
            string buildProjectId = Guid.NewGuid().ToString();

            Calculation calculation = CreateCalculation();
            calculation.BuildProjectId = buildProjectId;

            IQueryCollection queryStringValues = new QueryCollection(new Dictionary<string, StringValues>
            {
                { "calculationId", new StringValues(CalculationId) }
            });

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Query
                .Returns(queryStringValues);

            ILogger logger = CreateLogger();

            ICalculationsRepository calculationsRepository = CreateCalculationsRepository();
            calculationsRepository
                .GetCalculationById(Arg.Is(CalculationId))
                .Returns(calculation);

            CalculationService service = CreateCalculationService(logger: logger, calculationsRepository: calculationsRepository);

            //Act
            IActionResult result = await service.PublishCalculationVersion(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            logger
                .Received(1)
                .Warning(Arg.Is($"Build project for specification {calculation.Specification.Id} could not be found, creating a new one"));
        }

        static CalculationService CreateCalculationService(ICalculationsRepository calculationsRepository = null, 
            ILogger logger = null, ISearchRepository<CalculationIndex> serachRepository = null, IValidator<Calculation> calcValidator = null,
            IBuildProjectsRepository buildProjectsRepository = null)
        {
            return new CalculationService(calculationsRepository ?? CreateCalculationsRepository(), 
                logger ?? CreateLogger(), serachRepository ?? CreateSearchRepository(), calcValidator ?? CreateCalculationValidator(),
                buildProjectsRepository ?? CreateBuildProjectsRepository(), CreateSourceFileGeneratorProvider(), CreateCompilerFactory());
        }

        static ICalculationsRepository CreateCalculationsRepository()
        {
            return Substitute.For<ICalculationsRepository>();
        }

        static IBuildProjectsRepository CreateBuildProjectsRepository()
        {
            return Substitute.For<IBuildProjectsRepository>();
        }

        static ILogger CreateLogger()
        {
            return Substitute.For<ILogger>();
        }

        static ISearchRepository<CalculationIndex> CreateSearchRepository()
        {
            return Substitute.For<ISearchRepository<CalculationIndex>>();
        }

	    static ISourceFileGeneratorProvider CreateSourceFileGeneratorProvider()
	    {
		    return Substitute.For<ISourceFileGeneratorProvider>();
	    }

	    static ICompilerFactory CreateCompilerFactory()
	    {
            ICompiler compiler = Substitute.For<ICompiler>();

            ICompilerFactory compilerFactory = Substitute.For<ICompilerFactory>();
            compilerFactory
                .GetCompiler(Arg.Any<IEnumerable<SourceFile>>())
                .Returns(compiler);

            return compilerFactory;
	    }

		static IValidator<Calculation> CreateCalculationValidator(ValidationResult validationResult = null)
        {
            if (validationResult == null)
                validationResult = new ValidationResult();

            IValidator<Calculation> validator = Substitute.For<IValidator<Calculation>>();

            validator
               .ValidateAsync(Arg.Any<Calculation>())
               .Returns(validationResult);

            return validator;
        }

        static Calculation CreateCalculation()
        {
            return new Calculation
            {
                Id = CalculationId,
                Name = "Test Calc Name",
                CalculationSpecification = new Reference
                {
                    Id = "any-calc-id",
                    Name = "Test Calc Name",
                },
                Specification = new Models.Results.SpecificationSummary
                {
                    Id = "any-spec-id",
                    Name = "Test Spec Name",
                },
                Period = new Reference
                {
                    Id = "18/19",
                    Name = "2018/2019"
                },
                AllocationLine = new Reference
                {
                    Id = "test-alloc-id",
                    Name = "test-alloc-name"
                },
                Policies = new List<Reference>
                {
                    new Reference
                    {
                        Id = "policy-id",
                        Name = "policy-name"
                    }
                },
                Current = new CalculationVersion
                {
                    SourceCode = "source code",
                    PublishStatus = PublishStatus.Draft,
                },
                FundingStream = new Reference
                {
                    Id = "funding stream-id",
                    Name = "funding-stream-name"
                }
            };
        }
    }
}
