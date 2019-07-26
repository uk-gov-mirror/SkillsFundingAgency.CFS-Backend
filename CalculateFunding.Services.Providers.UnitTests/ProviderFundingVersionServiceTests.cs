﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CalculateFunding.Common.Caching;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Core.Interfaces.AzureStorage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Serilog;

namespace CalculateFunding.Services.Providers.UnitTests
{
    [TestClass]
    public class ProviderFundingVersionServiceTests
    {
        private const string providerFundingVersion = "id1";

        private string blobName = $"{providerFundingVersion}.json";

        [TestMethod]
        public async Task GetProviderFundingVersionsBody_GivenNullOrEmptyId_ReturnsBadRequest()
        {
            //Arrange
            string id = "";

            ProviderFundingVersionService service = CreateProviderFundingVersionService();

            //Act
            IActionResult result = await service.GetProviderFundingVersions(id);

            //Assert
            result
                .Should()
                .BeAssignableTo<BadRequestObjectResult>()
                .Which
                .Value
                .Should()
                .Be("Null or empty id provided.");
        }

        [TestMethod]
        public async Task GetProviderFundingVersionsBody_GivenBlobDoesNotExists_ReturnsNotFound()
        {
            //Arrange
            ILogger logger = CreateLogger();
            ICacheProvider cacheProvider = CacheProvider();

            IBlobClient blobClient = CreateBlobClient();
            blobClient
                .BlobExistsAsync(Arg.Is(blobName))
                .Returns(false);

            ProviderFundingVersionService service = CreateProviderFundingVersionService(cacheProvider,logger, blobClient);

            //Act
            IActionResult result = await service.GetProviderFundingVersions(providerFundingVersion);

            //Assert
            result
                .Should()
                .BeAssignableTo<NotFoundResult>();

            logger
                .Received(1)
                .Error(Arg.Is($"Blob '{blobName}' does not exist."));
        }

        [TestMethod]
        public async Task GetProviderFundingVersionsBody_GivenGetBlobReferenceCausesException_LogsAndReturnsInternalServerError()
        {
            //Arrange
            ILogger logger = CreateLogger();
            ICacheProvider cacheProvider = CacheProvider();

            IBlobClient blobClient = CreateBlobClient();
            blobClient
                .BlobExistsAsync(Arg.Is(blobName))
                .Returns(true);

            blobClient
                .When(x => x.GetBlobReferenceFromServerAsync(Arg.Is(blobName)))
                .Do(x => { throw new Exception(); });

            string errorMessage = $"Failed to fetch blob '{blobName}' from azure storage";

            ProviderFundingVersionService service = CreateProviderFundingVersionService(cacheProvider, logger, blobClient);

            //Act
            IActionResult result = await service.GetProviderFundingVersions(providerFundingVersion);

            //Assert
            result
                .Should()
                .BeAssignableTo<InternalServerErrorResult>()
                .Which
                .Value
                .Should()
                .Be(errorMessage);

            logger
                .Received(1)
                .Error(Arg.Any<Exception>(), Arg.Is(errorMessage));
        }

        [TestMethod]
        public async Task GetProviderFundingVersionsBody_GivenReturnsFromBlobStorage_returnsOK()
        {
            //Arrange
            string template = "just a string";

            byte[] bytes = Encoding.UTF8.GetBytes(template);

            Stream memoryStream = new MemoryStream(bytes);

            ILogger logger = CreateLogger();
            ICacheProvider cacheProvider = CacheProvider();
            ICloudBlob cloudBlob = CreateBlob();

            IBlobClient blobClient = CreateBlobClient();
            blobClient
                .BlobExistsAsync(Arg.Is(blobName))
                .Returns(true);

            blobClient
                 .GetBlobReferenceFromServerAsync(Arg.Is(blobName))
                 .Returns(cloudBlob);

            blobClient
                  .DownloadToStreamAsync(Arg.Is(cloudBlob))
                  .Returns(memoryStream);

            ProviderFundingVersionService service = CreateProviderFundingVersionService(cacheProvider,logger, blobClient);

            //Act
            IActionResult result = await service.GetProviderFundingVersions(providerFundingVersion);

            //Assert
            result
                .Should()
                .BeAssignableTo<OkObjectResult>()
                .Which
                .Value
                .Should()
                .Be(template);
        }

        private static ProviderFundingVersionService CreateProviderFundingVersionService(
                ICacheProvider cacheProvider = null,
                ILogger logger = null,
                IBlobClient blobClient = null)
        {
            return new ProviderFundingVersionService(
                cacheProvider ?? CacheProvider(),
                blobClient ?? CreateBlobClient(),
                logger ?? CreateLogger(),               
                ProviderResilienceTestHelper.GenerateTestPolicies());
        }

        private static ILogger CreateLogger()
        {
            return Substitute.For<ILogger>();
        }

        private static IBlobClient CreateBlobClient()
        {
            return Substitute.For<IBlobClient>();
        }

        private static ICloudBlob CreateBlob()
        {
            return Substitute.For<ICloudBlob>();
        }

        private static ICacheProvider CacheProvider()
        {
            return Substitute.For<ICacheProvider>();
        }
    }
}
