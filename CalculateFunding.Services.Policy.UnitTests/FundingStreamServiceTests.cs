﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CalculateFunding.Common.Caching;
using CalculateFunding.Models.Policy;
using CalculateFunding.Services.Core.Caching;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Policy.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Serilog;

namespace CalculateFunding.Services.Policy.UnitTests
{
    [TestClass]
    public class FundingStreamServiceTests
    {
        private const string yamlFile = "12345.yaml";

        [TestMethod]
        public async Task GetFundingStreams_GivenNullOrEmptyFundingStreamsReturned_LogsAndReturnsOKWithEmptyList()
        {
            // Arrange
            ILogger logger = CreateLogger();

            IEnumerable<FundingStream> fundingStreams = null;

            IPolicyRepository policyRepository = CreatePolicyRepository();
            policyRepository
                .GetFundingStreams()
                .Returns(fundingStreams);

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger, policyRepository: policyRepository);

            // Act
            IActionResult result = await fundingStreamsService.GetFundingStreams();

            // Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult objectResult = result as OkObjectResult;

            IEnumerable<FundingStream> values = objectResult.Value as IEnumerable<FundingStream>;

            values
                .Should()
                .NotBeNull();

            logger
                .Received(1)
                .Error(Arg.Is("No funding streams were returned"));
        }

        [TestMethod]
        public async Task GetFundingStreams_GivenFundingStreamsAlreadyInCache_ReturnsOKWithResultsFromCachel()
        {
            // Arrange
            ILogger logger = CreateLogger();

            IEnumerable<FundingStream> fundingStreams = new[]
            {
                new FundingStream(),
                new FundingStream()
            };

            ICacheProvider cacheProvider = CreateCacheProvider();
            cacheProvider
                .GetAsync<FundingStream[]>(Arg.Is(CacheKeys.AllFundingStreams))
                .Returns(fundingStreams.ToArray());

            IPolicyRepository policyRepository = CreatePolicyRepository();

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger, cacheProvider, policyRepository);

            // Act
            IActionResult result = await fundingStreamsService.GetFundingStreams();

            // Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult objectResult = result as OkObjectResult;

            IEnumerable<FundingStream> values = objectResult.Value as IEnumerable<FundingStream>;

            values
                .Should()
                .HaveCount(2);

            await
            policyRepository
                .DidNotReceive()
                .GetFundingStreams();
        }

        [TestMethod]
        public async Task GetFundingStreams_GivenFundingStreamsReturned_ReturnsOKWithResults()
        {
            // Arrange
            ILogger logger = CreateLogger();

            IEnumerable<FundingStream> fundingStreams = new[]
            {
                new FundingStream(),
                new FundingStream()
            };

            IPolicyRepository policyRepository = CreatePolicyRepository();
            policyRepository
                .GetFundingStreams()
                .Returns(fundingStreams);

            ICacheProvider cacheProvider = CreateCacheProvider();

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger, cacheProvider, policyRepository);

            // Act
            IActionResult result = await fundingStreamsService.GetFundingStreams();

            // Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult objectResult = result as OkObjectResult;

            IEnumerable<FundingStream> values = objectResult.Value as IEnumerable<FundingStream>;

            values
                .Should()
                .HaveCount(2);

            await
                cacheProvider
                    .Received(1)
                    .SetAsync<FundingStream[]>(Arg.Is(CacheKeys.AllFundingStreams), Arg.Is<FundingStream[]>(m => m.SequenceEqual(fundingStreams)));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task GetFundingStreamById_GivenFundingStreamIdDoesNotExist_ReturnsBadRequest(string fundingStreamId)
        {
            // Arrange
            ILogger logger = CreateLogger();

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger);

            // Act
            IActionResult result = await fundingStreamsService.GetFundingStreamById(fundingStreamId);

            // Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>()
                .Which
                .Value
                .Should()
                .Be("Null or empty funding stream Id provided");

            logger
                .Received(1)
                .Error(Arg.Is("No funding stream Id was provided to GetFundingStreamById"));
        }

        [TestMethod]
        public async Task GetFundingStreamById_GivenFundingStreamnWasNotFound_ReturnsNotFound()
        {
            // Arrange
            const string fundingStreamId = "fs-1";

            ILogger logger = CreateLogger();

            IPolicyRepository policyRepository = CreatePolicyRepository();
            policyRepository
                .GetFundingStreamById(Arg.Is(fundingStreamId))
                .Returns((FundingStream)null);

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger, policyRepository: policyRepository);

            // Act
            IActionResult result = await fundingStreamsService.GetFundingStreamById(fundingStreamId);

            // Assert
            result
                .Should()
                .BeOfType<NotFoundResult>();

            logger
                .Received(1)
                .Error(Arg.Is($"No funding stream was found for funding stream id : {fundingStreamId}"));
        }

        [TestMethod]
        public async Task GetFundingStreamById__GivenFundingStreamWasFound_ReturnsSuccess()
        {
            // Arrange
            const string fundingStreamId = "fs-1";

            FundingStream fundingStream = new FundingStream
            {
                Id = fundingStreamId,
                Name = "Funding Stream Name",
                ShortName = "FSN",
            };

            IPolicyRepository policyRepository = CreatePolicyRepository();
            policyRepository
                .GetFundingStreamById(Arg.Is(fundingStreamId))
                .Returns(fundingStream);

            FundingStreamService fundingStreamsService = CreateFundingStreamService(policyRepository: policyRepository);

            // Act
            IActionResult result = await fundingStreamsService.GetFundingStreamById(fundingStreamId);

            // Assert
            result
                .Should()
                .BeOfType<OkObjectResult>()
                .Which
                .Value
                .Should()
                .Be(fundingStream);
        }

        [TestMethod]
        async public Task SaveFundingStream_GivenNoYamlWasProvidedWithNoFileName_ReturnsBadRequest()
        {
            //Arrange
            HttpRequest request = Substitute.For<HttpRequest>();

            ILogger logger = CreateLogger();

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger);

            //Act
            IActionResult result = await fundingStreamsService.SaveFundingStream(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is($"Null or empty yaml provided for file: File name not provided"));
        }

        [TestMethod]
        async public Task SaveFundingStream_GivenNoYamlWasProvidedButFileNameWas_ReturnsBadRequest()
        {
            //Arrange
            IHeaderDictionary headerDictionary = new HeaderDictionary
            {
                { "yaml-file", new StringValues(yamlFile) }
            };

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Headers
                .Returns(headerDictionary);

            ILogger logger = CreateLogger();

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger);

            //Act
            IActionResult result = await fundingStreamsService.SaveFundingStream(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Is($"Null or empty yaml provided for file: {yamlFile}"));
        }

        [TestMethod]
        async public Task SaveFundingStream_GivenNoYamlWasProvidedButIsInvalid_ReturnsBadRequest()
        {
            //Arrange
            string yaml = "invalid yaml";
            byte[] byteArray = Encoding.UTF8.GetBytes(yaml);
            MemoryStream stream = new MemoryStream(byteArray);

            IHeaderDictionary headerDictionary = new HeaderDictionary
            {
                { "yaml-file", new StringValues(yamlFile) }
            };

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Headers
                .Returns(headerDictionary);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger);

            //Act
            IActionResult result = await fundingStreamsService.SaveFundingStream(request);

            //Assert
            result
                .Should()
                .BeOfType<BadRequestObjectResult>();

            logger
                .Received(1)
                .Error(Arg.Any<Exception>(), Arg.Is($"Invalid yaml was provided for file: {yamlFile}"));
        }

        [TestMethod]
        async public Task SaveFundingStream_GivenValidYamlButFailedToSaveToDatabase_ReturnsStatusCode()
        {
            //Arrange
            string yaml = CreateRawFundingStream();
            byte[] byteArray = Encoding.UTF8.GetBytes(yaml);
            MemoryStream stream = new MemoryStream(byteArray);

            IHeaderDictionary headerDictionary = new HeaderDictionary
            {
                { "yaml-file", new StringValues(yamlFile) }
            };

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Headers
                .Returns(headerDictionary);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            HttpStatusCode failedCode = HttpStatusCode.BadGateway;

            IPolicyRepository policyRepository = CreatePolicyRepository();
            policyRepository
                .SaveFundingStream(Arg.Any<FundingStream>())
                .Returns(failedCode);

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger, policyRepository: policyRepository);

            //Act
            IActionResult result = await fundingStreamsService.SaveFundingStream(request);

            //Assert
            result
                .Should()
                .BeOfType<StatusCodeResult>();

            StatusCodeResult statusCodeResult = (StatusCodeResult)result;
            statusCodeResult
                .StatusCode
                .Should()
                .Be(502);

            logger
                .Received(1)
                .Error(Arg.Is($"Failed to save yaml file: {yamlFile} to cosmos db with status 502"));
        }

        [TestMethod]
        async public Task SaveFundingStream_GivenValidYamlButSavingToDatabaseThrowsException_ReturnsInternalServerError()
        {
            //Arrange
            string yaml = CreateRawFundingStream();
            byte[] byteArray = Encoding.UTF8.GetBytes(yaml);
            MemoryStream stream = new MemoryStream(byteArray);

            IHeaderDictionary headerDictionary = new HeaderDictionary
            {
                { "yaml-file", new StringValues(yamlFile) }
            };

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Headers
                .Returns(headerDictionary);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            IPolicyRepository policyRepository = CreatePolicyRepository();
            policyRepository
                .When(x => x.SaveFundingStream(Arg.Any<FundingStream>()))
                .Do(x => { throw new Exception(); });

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger, policyRepository: policyRepository);

            string expectedErrorMessage = $"Exception occurred writing to yaml file: {yamlFile} to cosmos db";

            //Act
            IActionResult result = await fundingStreamsService.SaveFundingStream(request);

            //Assert
            result
                .Should()
                .BeOfType<InternalServerErrorResult>()
                .Which
                .Value
                .Should()
                .Be(expectedErrorMessage);

            logger
                .Received(1)
                .Error(Arg.Any<Exception>(), Arg.Is(expectedErrorMessage));
        }

        [TestMethod]
        async public Task SaveFundingStream_GivenValidYamlAndSaveWasSuccesful_ReturnsOK()
        {
            //Arrange
            string yaml = CreateRawFundingStream();
            byte[] byteArray = Encoding.UTF8.GetBytes(yaml);
            MemoryStream stream = new MemoryStream(byteArray);

            IHeaderDictionary headerDictionary = new HeaderDictionary
            {
                { "yaml-file", new StringValues(yamlFile) }
            };

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Headers
                .Returns(headerDictionary);

            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            HttpStatusCode statusCode = HttpStatusCode.Created;

            IPolicyRepository policyRepository = CreatePolicyRepository();
            policyRepository
                .SaveFundingStream(Arg.Any<FundingStream>())
                .Returns(statusCode);

            FundingStreamService fundingStreamsService = CreateFundingStreamService(logger: logger, policyRepository: policyRepository);

            //Act
            IActionResult result = await fundingStreamsService.SaveFundingStream(request);

            //Assert
            result
                .Should()
                .BeOfType<OkResult>();

            logger
                .Received(1)
                .Information(Arg.Is($"Successfully saved file: {yamlFile} to cosmos db"));
        }

        private static FundingStreamService CreateFundingStreamService(
            ILogger logger = null,
            ICacheProvider cacheProvider = null,
            IPolicyRepository policyRepository = null)
        {
            return new FundingStreamService(
                logger ?? CreateLogger(),
                cacheProvider ?? CreateCacheProvider(),
                policyRepository ?? CreatePolicyRepository(),
                PolicyResiliencePoliciesTestHelper.GenerateTestPolicies());
        }

        private static ILogger CreateLogger()
        {
            return Substitute.For<ILogger>();
        }

        private static IPolicyRepository CreatePolicyRepository()
        {
            return Substitute.For<IPolicyRepository>();
        }

        private static ICacheProvider CreateCacheProvider()
        {
            return Substitute.For<ICacheProvider>();
        }

        private static string CreateRawFundingStream()
        {
            StringBuilder yaml = new StringBuilder();

            yaml.AppendLine(@"id: YPLRE");
            yaml.AppendLine(@"name: School Budget Share");
            yaml.AppendLine(@"shortName: SBS");

            return yaml.ToString();
        }
    }
}
