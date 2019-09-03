using System;
using System.Collections.Generic;
using CalculateFunding.Api.External.V3.Interfaces;
using CalculateFunding.Api.External.V3.Services;
using CalculateFunding.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CalculateFunding.Api.External.UnitTests
{
    [TestClass]
    public class StartupTests : IoCUnitTestBase
    {
        [TestMethod]
        public void ConfigureServices_RegisterDependenciesCorrectly()
        {
            // Arrange
            IConfigurationRoot configuration = CreateTestConfiguration();
            Startup target = new Startup(configuration);

            // Act
            target.ConfigureServices(Services);

            // Assert v3
            ResolveType<V3.Controllers.FundingFeedController>().Should().NotBeNull(nameof(V3.Controllers.FundingFeedController));
            ResolveType<V3.Controllers.FundingFeedItemController>().Should().NotBeNull(nameof(V3.Controllers.FundingFeedItemController));
            ResolveType<V3.Controllers.ProviderFundingVersionController>().Should().NotBeNull(nameof(V3.Controllers.ProviderFundingVersionController));
        }

        [TestMethod]
        public void ConfigureServices_WhenMajorMinorVersioningIsEnabled_RegisterDependenciesCorrectly()
        {
            // Arrange
            IConfigurationRoot configuration = CreateTestConfiguration();
            Startup target = new Startup(configuration);

            // Act
            target.ConfigureServices(Services);

            // Assert
            IServiceProvider serviceProvider = target.ServiceProvider;

            IProviderFundingVersionService service = (IProviderFundingVersionService)serviceProvider.GetService(typeof(IProviderFundingVersionService));

            service
                .Should()
                .BeOfType<ProviderFundingVersionService>();
        }

        protected override Dictionary<string, string> AddToConfiguration()
        {
            var configData = new Dictionary<string, string>
            {
                { "SearchServiceName", "ss-t1te-cfs"},
                { "SearchServiceKey", "test" },
                { "CosmosDbSettings:CollectionName", "providerresults" },
                { "CosmosDbSettings:DatabaseName", "calculate-funding" },
                { "CosmosDbSettings:ConnectionString", "AccountEndpoint=https://test.documents.azure.com:443/;AccountKey=dGVzdA==;" },
                { "AzureStorageSettings:ConnectionString", "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=AccountKey=dGVzdA==;EndpointSuffix=core.windows.net" },
                { "specificationsClient:ApiEndpoint", "https://localhost:7001/api/" },
                { "specificationsClient:ApiKey", "Local" },
                { "resultsClient:ApiEndpoint", "https://localhost:7005/api/" },
                { "resultsClient:ApiKey", "Local" },
                { "jobsClient:ApiKey", "Local" },
                { "jobsClient:ApiEndpoint", "https://localhost:7010/api/" },
                { "providerProfilingClient:ApiEndpoint", "https://funding-profiling/" },
                { "providerProfilingAzureBearerTokenOptions:Url", "https://wahetever-token" },
                { "providerProfilingAzureBearerTokenOptions:GrantType", "client_credentials" },
                { "providerProfilingAzureBearerTokenOptions:Scope", "https://wahetever-scope" },
                { "providerProfilingAzureBearerTokenOptions:ClientId", "client-id" },
                { "providerProfilingAzureBearerTokenOptions:ClientSecret", "client-secret"},
            };

            return configData;
        }
    }
}