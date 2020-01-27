﻿using System.Collections.Generic;
using CalculateFunding.Functions.Publishing.ServiceBus;
using CalculateFunding.Services.Publishing.Interfaces;
using CalculateFunding.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CalculateFunding.Functions.Publishing.UnitTests
{
    [TestClass]
    public class StartupTests : IoCUnitTestBase
    {
        [TestMethod]
        public void ConfigureServices_RegisterDependenciesCorrectly()
        {
            // Arrange
            IConfigurationRoot configuration = CreateTestConfiguration();

            // Act
            using (IServiceScope scope = Startup.RegisterComponents(new ServiceCollection(), configuration).CreateScope())
            {
                // Assert
                scope.ServiceProvider.GetService<IPublishService>().Should().NotBeNull(nameof(IPublishService));
                scope.ServiceProvider.GetService<IApproveService>().Should().NotBeNull(nameof(IApproveService));
                scope.ServiceProvider.GetService<IRefreshService>().Should().NotBeNull(nameof(IRefreshService));
                scope.ServiceProvider.GetService<ICalculationResultsRepository>().Should().NotBeNull(nameof(ICalculationResultsRepository));
                scope.ServiceProvider.GetService<IRefreshPrerequisiteChecker>().Should().NotBeNull(nameof(IRefreshPrerequisiteChecker));
                scope.ServiceProvider.GetService<IPublishedSearchService>().Should().NotBeNull(nameof(IPublishedSearchService));
            }
        }

        protected override Dictionary<string, string> AddToConfiguration()
        {
            Dictionary<string, string> configData = new Dictionary<string, string>
            {
                { "SearchServiceName", "ss-t1te-cfs"},
                { "SearchServiceKey", "test" },
                { "CosmosDbSettings:DatabaseName", "calculate-funding" },
                { "CosmosDbSettings:ContainerName", "calcs" },
                { "CosmosDbSettings:ConnectionString", "AccountEndpoint=https://test.documents.azure.com:443/;AccountKey=dGVzdA==;" },
                { "profilingClient:BaseUrl", "https://localhost:5003" },
                { "profilingClient:ApiKey", "Test" },
                { "jobsClient:ApiEndpoint", "https://localhost:7010/api/"},
                { "jobsClient:ApiKey", "Local"},
                { "providersClient:ApiEndpoint", "https://localhost:7011/api/" },
                { "providersClient:ApiKey", "Local" },
                { "calcsClient:ApiEndpoint", "https://localhost:7002/api/" },
                { "calcsClient:ApiKey", "Local" },
                { "providerProfilingClient:ApiEndpoint", "https://funding-profiling/" },
                { "providerProfilingAzureBearerTokenOptions:Url", "https://wahetever-token" },
                { "providerProfilingAzureBearerTokenOptions:GrantType", "client_credentials" },
                { "providerProfilingAzureBearerTokenOptions:Scope", "https://wahetever-scope" },
                { "providerProfilingAzureBearerTokenOptions:ClientId", "client-id" },
                { "providerProfilingAzureBearerTokenOptions:ClientSecret", "client-secret"}
            };

            return configData;
        }
    }
}
