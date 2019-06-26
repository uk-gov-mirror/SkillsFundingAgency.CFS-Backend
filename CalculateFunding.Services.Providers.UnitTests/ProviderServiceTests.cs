using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.Caching;
using CalculateFunding.Models.MappingProfiles;
using CalculateFunding.Models.Providers;
using CalculateFunding.Models.Results;
using CalculateFunding.Models.Specs;
using CalculateFunding.Services.Core.Caching;
using CalculateFunding.Services.Core.Interfaces.Proxies;
using CalculateFunding.Services.Providers.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace CalculateFunding.Services.Providers.UnitTests
{
    [TestClass]
    public class ProviderServiceTests
    {
        [TestMethod]
        public async Task PopulateProviderSummariesForSpecification_GivenSpecificationWithProviderVersionId_TotalCountOfProvidersReturned()
        {
            //Arrange
            string specificationId = Guid.NewGuid().ToString();
            string providerVersionId = Guid.NewGuid().ToString();
            string cacheKey = $"{CacheKeys.ScopedProviderSummariesPrefix}{specificationId}";

            Provider provider = CreateProvider();

            ProviderVersion providerVersion = new ProviderVersion
            {
                Providers = new List<Provider> { provider }
            };

            SpecificationSummary specificationSummary = new SpecificationSummary
            {
                Id = specificationId,
                ProviderVersionId = providerVersionId
            };

            ISpecificationsApiClientProxy specificationsApiClientProxy = CreateSpecificationsApiClientProxy();
            specificationsApiClientProxy
                .GetAsync<SpecificationSummary>(Arg.Any<string>())
                .Returns(specificationSummary);

            IProviderVersionService providerVersionService = CreateProviderVersionService();
            providerVersionService
                .GetProvidersByVersion(Arg.Is(providerVersionId))
                .Returns(providerVersion);

            IResultsApiClientProxy resultsApiClient = CreateResultsApiClient();
            resultsApiClient
                .GetAsync<IEnumerable<string>>(Arg.Any<string>())
                .Returns(new List<string> { { "1234" } });

            ICacheProvider cacheProvider = CreateCacheProvider();

            IScopedProvidersService providerService = CreateProviderService(resultsApiClient: resultsApiClient, specificationsApiClient: specificationsApiClientProxy, providerVersionService: providerVersionService, cacheProvider: cacheProvider);

            //Act
            IActionResult totalCountResult  = await providerService.PopulateProviderSummariesForSpecification(specificationId);

            await specificationsApiClientProxy
                .Received(1)
                .GetAsync<SpecificationSummary>(Arg.Any<string>());

            await providerVersionService
                .Received(1)
                .GetProvidersByVersion(Arg.Is(providerVersionId));

            await cacheProvider
                .Received(1)
                .CreateListAsync<ProviderSummary>(Arg.Is<IEnumerable<ProviderSummary>>(x =>
                    x.First().Id == provider.ProviderId &&
                    x.First().Name == provider.Name &&
                    x.First().ProviderProfileIdType == provider.ProviderProfileIdType &&
                    x.First().UKPRN == provider.UKPRN &&
                    x.First().URN == provider.URN &&
                    x.First().Authority == provider.Authority &&
                    x.First().UPIN == provider.UPIN &&
                    x.First().ProviderSubType == provider.ProviderSubType &&
                    x.First().EstablishmentNumber == provider.EstablishmentNumber &&
                    x.First().ProviderType == provider.ProviderType &&
                    x.First().DateOpened == provider.DateOpened &&
                    x.First().DateClosed == provider.DateClosed &&
                    x.First().LACode == provider.LACode &&
                    x.First().CrmAccountId == provider.CrmAccountId &&
                    x.First().LegalName == provider.LegalName &&
                    x.First().NavVendorNo == provider.NavVendorNo &&
                    x.First().DfeEstablishmentNumber == provider.DfeEstablishmentNumber &&
                    x.First().Status == provider.Status &&
                    x.First().PhaseOfEducation == provider.PhaseOfEducation &&
                    x.First().ReasonEstablishmentClosed == provider.ReasonEstablishmentClosed &&
                    x.First().ReasonEstablishmentOpened == provider.ReasonEstablishmentOpened &&
                    x.First().Successor == provider.Successor &&
                    x.First().TrustStatus == Enum.Parse<Models.Results.TrustStatus>(provider.TrustStatusViewModelString) &&
                    x.First().TrustName == provider.TrustName &&
                    x.First().TrustCode == provider.TrustCode
                ), Arg.Is(cacheKey));

            totalCountResult
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult objectResult = totalCountResult as OkObjectResult;


            int? totalCount = objectResult.Value as int?;

            totalCount
                .Should()
                .Be(1);
        }

        [TestMethod]
        public async Task FetchCoreProviderData_WhenInCache_ThenReturnsCacheValue()
        {
            // Arrange
            string specificationId = Guid.NewGuid().ToString();
            string providerVersionId = Guid.NewGuid().ToString();
            string cacheKeyAllProviderSummaryCount = $"{CacheKeys.AllProviderSummaryCount}{specificationId}";
            string cacheKeyAllProviderSummaries = $"{CacheKeys.AllProviderSummaries}{specificationId}";

            Provider provider = CreateProvider();

            ProviderVersion providerVersion = new ProviderVersion
            {
                Providers = new List<Provider> { provider }
            };

            List<ProviderSummary> cachedProviderSummaries = new List<ProviderSummary>
            {
                MapProviderToSummary(provider)
            };

            SpecificationSummary specificationSummary = new SpecificationSummary
            {
                ProviderVersionId = providerVersionId
            };

            ICacheProvider cacheProvider = CreateCacheProvider();
            cacheProvider
                .GetAsync<string>(Arg.Is(cacheKeyAllProviderSummaryCount))
                .Returns("1");

            cacheProvider
                .ListRangeAsync<ProviderSummary>(Arg.Is(cacheKeyAllProviderSummaries), Arg.Is(0), Arg.Is(1))
                .Returns(cachedProviderSummaries);

            cacheProvider
                .ListLengthAsync<ProviderSummary>(Arg.Is(cacheKeyAllProviderSummaries))
                .Returns(1);

            ISpecificationsApiClientProxy specificationsApiClient = CreateSpecificationsApiClientProxy();
            specificationsApiClient
                .GetAsync<SpecificationSummary>(Arg.Any<string>())
                .Returns(specificationSummary);


            IProviderVersionService providerVersionService = CreateProviderVersionService();

            IScopedProvidersService providerService = CreateProviderService(cacheProvider: cacheProvider, providerVersionService: providerVersionService, specificationsApiClient: specificationsApiClient);

            providerVersionService
                .GetProvidersByVersion(Arg.Is(providerVersionId))
                .Returns(providerVersion);

            // Act
            IActionResult result = await providerService.FetchCoreProviderData(specificationId);

            // Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult okObjectResult = result as OkObjectResult;

            IEnumerable<ProviderSummary> results = okObjectResult.Value as IEnumerable<ProviderSummary>;
            results.Should().Contain(cachedProviderSummaries);
        }

        [TestMethod]
        public async Task FetchCoreProviderData_WhenNotInCache_ThenReturnsProviderVersion()
        {
            // Arrange
            string specificationId = Guid.NewGuid().ToString();
            string providerVersionId = Guid.NewGuid().ToString();
            string cacheKeyAllProviderSummaryCount = $"{CacheKeys.AllProviderSummaryCount}{specificationId}";
            string cacheKeyAllProviderSummaries = $"{CacheKeys.AllProviderSummaries}{specificationId}";

            ICacheProvider cacheProvider = CreateCacheProvider();
            cacheProvider
                .GetAsync<string>(Arg.Is(cacheKeyAllProviderSummaryCount))
                .Returns("0");

            Provider provider = CreateProvider();

            ProviderVersion providerVersion = new ProviderVersion
            {
                Providers = new List<Provider> { provider }
            };

            SpecificationSummary specificationSummary = new SpecificationSummary
            {
                ProviderVersionId = providerVersionId
            };

            ISpecificationsApiClientProxy specificationsApiClient = CreateSpecificationsApiClientProxy();
            specificationsApiClient
                .GetAsync<SpecificationSummary>(Arg.Any<string>())
                .Returns(specificationSummary);

            IProviderVersionService providerVersionService = CreateProviderVersionService();

            IScopedProvidersService providerService = CreateProviderService(cacheProvider: cacheProvider, specificationsApiClient: specificationsApiClient, providerVersionService: providerVersionService);

            providerVersionService
                .GetProvidersByVersion(Arg.Is(providerVersionId))
                .Returns(providerVersion);

            // Act
            IActionResult result = await providerService.FetchCoreProviderData(specificationId);

            // Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult okObjectResult = result as OkObjectResult;

            IEnumerable<ProviderSummary> results = okObjectResult.Value as IEnumerable<ProviderSummary>;

            results
               .Should()
               .HaveCount(1);

            results.Should().Contain(r => r.Id == "1234");
        }

        [TestMethod]
        public async Task FetchCoreProviderData_WhenNotInCache_ThenAddsToCache()
        {
            // Arrange
            string specificationId = Guid.NewGuid().ToString();
            string providerVersionId = Guid.NewGuid().ToString();
            string cacheKeyAllProviderSummaryCount = $"{CacheKeys.AllProviderSummaryCount}{specificationId}";
            string cacheKeyAllProviderSummaries = $"{CacheKeys.AllProviderSummaries}{specificationId}"; ;

            Provider provider = CreateProvider();

            ProviderVersion providerVersion = new ProviderVersion
            {
                Providers = new List<Provider> { provider }
            };

            List<ProviderSummary> cachedProviderSummaries = new List<ProviderSummary>
            {
                MapProviderToSummary(provider)
            };

            SpecificationSummary specificationSummary = new SpecificationSummary
            {
                ProviderVersionId = providerVersionId
            };

            ISpecificationsApiClientProxy specificationsApiClient = CreateSpecificationsApiClientProxy();
            specificationsApiClient
                .GetAsync<SpecificationSummary>(Arg.Any<string>())
                .Returns(specificationSummary);

            ICacheProvider cacheProvider = CreateCacheProvider();
            cacheProvider
                .GetAsync<string>(Arg.Is(cacheKeyAllProviderSummaryCount))
                .Returns("0");

            IProviderVersionService providerVersionService = CreateProviderVersionService();
            providerVersionService
                .GetProvidersByVersion(Arg.Is(providerVersionId))
                .Returns(providerVersion);

            IScopedProvidersService providerService = CreateProviderService(cacheProvider: cacheProvider, providerVersionService: providerVersionService, specificationsApiClient: specificationsApiClient);

            // Act
            IActionResult result = await providerService.FetchCoreProviderData(specificationId);

            // Assert
            await cacheProvider
                .Received(1)
                .KeyDeleteAsync<ProviderSummary>(Arg.Is(cacheKeyAllProviderSummaries));

            await cacheProvider
                .Received(1)
                .CreateListAsync(Arg.Is<IEnumerable<ProviderSummary>>(l => l.Count() == 1), Arg.Is(cacheKeyAllProviderSummaries));
        }

        private IScopedProvidersService CreateProviderService(IProviderVersionService providerVersionService = null, ISpecificationsApiClientProxy specificationsApiClient = null, ICacheProvider cacheProvider = null, IResultsApiClientProxy resultsApiClient = null)
        {
            return new ScopedProvidersService(
                cacheProvider ?? CreateCacheProvider(),
                resultsApiClient ?? CreateResultsApiClient(),
                specificationsApiClient ?? CreateSpecificationsApiClientProxy(),
                providerVersionService ?? CreateProviderVersionService(),
                CreateMapper());
        }

        private IProviderVersionService CreateProviderVersionService()
        {
            return Substitute.For<IProviderVersionService>();
        }

        private ISpecificationsApiClientProxy CreateSpecificationsApiClientProxy()
        {
            return Substitute.For<ISpecificationsApiClientProxy>();
        }

        private ICacheProvider CreateCacheProvider()
        {
            return Substitute.For<ICacheProvider>();
        }

        private IResultsApiClientProxy CreateResultsApiClient()
        {
            return Substitute.For<IResultsApiClientProxy>();
        }

        private IMapper CreateMapper()
        {
            MapperConfiguration mapperConfiguration = new MapperConfiguration(c => c.AddProfile<ProviderVersionsMappingProfile>());
            return mapperConfiguration.CreateMapper();
        }

        private ProviderSummary MapProviderToSummary(Provider provider)
        {
            return new ProviderSummary
            {
                Id = provider.ProviderId,
                Name = provider.Name,
                ProviderProfileIdType = provider.ProviderProfileIdType,
                UKPRN = provider.UKPRN,
                URN = provider.URN,
                Authority = provider.Authority,
                UPIN = provider.UPIN,
                ProviderSubType = provider.ProviderSubType,
                EstablishmentNumber = provider.EstablishmentNumber,
                ProviderType = provider.ProviderType,
                DateOpened = provider.DateOpened,
                DateClosed = provider.DateClosed,
                LACode = provider.LACode,
                CrmAccountId = provider.CrmAccountId,
                LegalName = provider.LegalName,
                NavVendorNo = provider.NavVendorNo,
                DfeEstablishmentNumber = provider.DfeEstablishmentNumber,
                Status = provider.Status,
                PhaseOfEducation = provider.PhaseOfEducation,
                ReasonEstablishmentClosed = provider.ReasonEstablishmentClosed,
                ReasonEstablishmentOpened = provider.ReasonEstablishmentOpened,
                Successor = provider.Successor,
                TrustStatus = Enum.Parse<Models.Results.TrustStatus>(provider.TrustStatusViewModelString),
                TrustName = provider.TrustName,
                TrustCode = provider.TrustCode
            };
        }

        private Provider CreateProvider()
        {
            return new Provider
            {
                Name = "provider name",
                ProviderId = "1234",
                ProviderProfileIdType = "provider id type",
                UKPRN = "UKPRN",
                URN = "URN",
                Authority = "Authority",
                UPIN = "UPIN",
                ProviderSubType = "ProviderSubType",
                EstablishmentNumber = "EstablishmentNumber",
                ProviderType = "ProviderType",
                DateOpened = DateTime.UtcNow,
                DateClosed = DateTime.UtcNow,
                LACode = "LACode",
                CrmAccountId = "CrmAccountId",
                LegalName = "LegalName",
                NavVendorNo = "NavVendorNo",
                DfeEstablishmentNumber = "DfeEstablishmentNumber",
                Status = "Status",
                PhaseOfEducation = "PhaseOfEducation",
                ReasonEstablishmentClosed = "ReasonEstablishmentClosed",
                ReasonEstablishmentOpened = "ReasonEstablishmentOpened",
                Successor = "Successor",
                TrustStatusViewModelString = "NotApplicable",
                TrustName = "TrustName",
                TrustCode = "TrustCode"
            };
        }
    }
}
