using System;
using System.Collections.Generic;
using CalculateFunding.Api.Results.Controllers;
using CalculateFunding.Services.Results;
using CalculateFunding.Services.Results.Interfaces;
using CalculateFunding.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace CalculateFunding.Api.Results.UnitTests
{
    [TestClass]
    public class StartupTests : IoCUnitTestBase
    {
        [TestMethod]
        public void ConfigureServices_RegisterDependenciesCorrectly()
        {
            // Arrange
            Services.AddSingleton<IHostingEnvironment>(new HostingEnvironment());
            IConfigurationRoot configuration = CreateTestConfiguration();
            Startup target = new Startup(configuration);

            // Act
            target.ConfigureServices(Services);

            // Assert
            ResolveType<ResultsController>().Should().NotBeNull(nameof(ResultsController));
        }

        protected override Dictionary<string, string> AddToConfiguration()
        {
            var configData = new Dictionary<string, string>
            {
                { "SearchServiceName", "ss-t1te-cfs"},
                { "SearchServiceKey", "test" },
                { "CosmosDbSettings:ContainerName", "providerresults" },
                { "CosmosDbSettings:DatabaseName", "calculate-funding" },
                { "CosmosDbSettings:ConnectionString", "AccountEndpoint=https://test.documents.azure.com:443/;AccountKey=dGVzdA==;" },
                { "specificationsClient:ApiEndpoint", "https://localhost:7001/api/" },
                { "specificationsClient:ApiKey", "Local" },
                { "calcsClient:ApiEndpoint", "https://localhost:7002/api/" },
                { "calcsClient:ApiKey", "Local" },
                { "jobsClient:ApiKey", "Local" },
                { "jobsClient:ApiEndpoint", "https://localhost:7010/api/" },
            };

            return configData;
        }
    }
}