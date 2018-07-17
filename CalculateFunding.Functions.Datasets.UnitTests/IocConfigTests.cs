using CalculateFunding.Services.DataImporter;
using CalculateFunding.Services.Datasets.Interfaces;
using CalculateFunding.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace CalculateFunding.Functions.Datasets.UnitTests
{
    [TestClass]
    public class IocConfigTests : IoCUnitTestBase
    {
        [TestMethod]
        public void ConfigureServices_RegisterDependenciesCorrectly()
        {
            // Arrange
            IConfigurationRoot configuration = CreateTestConfiguration();

            // Act
            using (var scope = IocConfig.Build(configuration).CreateScope())
            {
                // Assert
                scope.ServiceProvider.GetService<IDefinitionsService>().Should().NotBeNull(nameof(IDefinitionsService));
                scope.ServiceProvider.GetService<IDatasetService>().Should().NotBeNull(nameof(IDatasetService));
                scope.ServiceProvider.GetService<IDatasetRepository>().Should().NotBeNull(nameof(IDatasetRepository));
                scope.ServiceProvider.GetService<IDatasetSearchService>().Should().NotBeNull(nameof(IDatasetSearchService));
                scope.ServiceProvider.GetService<IDefinitionSpecificationRelationshipService>().Should().NotBeNull(nameof(IDefinitionSpecificationRelationshipService));
                scope.ServiceProvider.GetService<ISpecificationsRepository>().Should().NotBeNull(nameof(ISpecificationsRepository));
                scope.ServiceProvider.GetService<IExcelDatasetReader>().Should().NotBeNull(nameof(IExcelDatasetReader));
                scope.ServiceProvider.GetService<IProviderRepository>().Should().NotBeNull(nameof(IProviderRepository));
                scope.ServiceProvider.GetService<ICalcsRepository>().Should().NotBeNull(nameof(ICalcsRepository));
            }
        }

        protected override Dictionary<string, string> AddToConfiguration()
        {
            var configData = new Dictionary<string, string>
            {
                { "SearchServiceName", "ss-t1te-cfs"},
                { "SearchServiceKey", "test" },
                { "CosmosDbSettings:DatabaseName", "calculate-funding" },
                { "CosmosDbSettings:CollectionName", "calcs" },
                { "CosmosDbSettings:ConnectionString", "AccountEndpoint=https://test.documents.azure.com:443/;AccountKey=dGVzdA==;" },
                { "specificationsClient:ApiEndpoint", "https://localhost:7001/api/" },
                { "specificationsClient:ApiKey", "Local" },
                { "resultsClient:ApiEndpoint", "https://localhost:7005/api/" },
                { "resultsClient:ApiKey", "Local" },
                { "calcsClient:ApiEndpoint", "https://localhost:7002/api/" },
                { "calcsClient:ApiKey", "Local" }
            };

            return configData;
        }
    }
}
