﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.Caching;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models;
using CalculateFunding.Models.Providers;
using CalculateFunding.Models.Results;
using CalculateFunding.Models.Specs;
using CalculateFunding.Services.Core.Caching;
using CalculateFunding.Services.Core.Interfaces.Proxies;
using CalculateFunding.Services.Providers.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CalculateFunding.Services.Providers
{
    public class ScopedProvidersService : IScopedProvidersService
    {
        private const string getSpecificationSummary = "specs/specification-summary-by-id?specificationId={0}";
        private const string getProviderVersion = "providers/versions/{0}";
        private const string getScopedProviderIdsUrl = "results/get-scoped-providerids?specificationId=";

        private readonly ICacheProvider _cacheProvider;
        private readonly IResultsApiClientProxy _resultsApiClient;
        private readonly ISpecificationsApiClientProxy _specificationsApiClient;
        private readonly IProviderVersionService _providerVersionService;
        private readonly IMapper _mapper;

        public ScopedProvidersService(ICacheProvider cacheProvider, IResultsApiClientProxy resultsApiClient, ISpecificationsApiClientProxy specificationsApiClient, IProviderVersionService providerVersionService, IMapper mapper)
        {
            Guard.ArgumentNotNull(cacheProvider, nameof(cacheProvider));
            Guard.ArgumentNotNull(resultsApiClient, nameof(resultsApiClient));
            Guard.ArgumentNotNull(specificationsApiClient, nameof(specificationsApiClient));
            Guard.ArgumentNotNull(providerVersionService, nameof(providerVersionService));
            Guard.ArgumentNotNull(mapper, nameof(mapper));

            _cacheProvider = cacheProvider;
            _resultsApiClient = resultsApiClient;
            _specificationsApiClient = specificationsApiClient;
            _providerVersionService = providerVersionService;
            _mapper = mapper;
        }

        public async Task<IActionResult> PopulateProviderSummariesForSpecification(string specificationId)
        {
            Guard.IsNullOrWhiteSpace(specificationId, nameof(specificationId));

            string cacheKeyAllProviderSummaryCount = $"{CacheKeys.AllProviderSummaryCount}{specificationId}";
            string cacheKeyAllProviderSummaries = $"{CacheKeys.AllProviderSummaries}{specificationId}";
            string cacheKey = $"{CacheKeys.ScopedProviderSummariesPrefix}{specificationId}";

            IEnumerable<ProviderSummary> allCachedProviders = Enumerable.Empty<ProviderSummary>();

            string providerCount = await _cacheProvider.GetAsync<string>(cacheKeyAllProviderSummaryCount);
            int allSummariesCount = 0;

            if (!string.IsNullOrWhiteSpace(providerCount) && !int.TryParse(providerCount, out allSummariesCount))
            {
                throw new Exception("Invalid provider count in cache");
            }

            if (allSummariesCount > 0)
            {
                allCachedProviders = await _cacheProvider.ListRangeAsync<ProviderSummary>(cacheKeyAllProviderSummaries, 0, allSummariesCount);
            }

            if (allSummariesCount < 1 || allCachedProviders.IsNullOrEmpty())
            {
                allCachedProviders = await GetScopedProvidersBySpecification(specificationId);
                allSummariesCount = allCachedProviders.Count();
            }

            if (allSummariesCount < 1 || allCachedProviders.IsNullOrEmpty())
            {
                throw new InvalidOperationException($"No provider summaries exist in cache or provider versions");
            }

            IEnumerable<string> providerIds = await GetScopedProviderIdsBySpecification(specificationId);

            int providerIdCount = providerIds.Count();

            IList<ProviderSummary> providerSummaries = new List<ProviderSummary>(providerIdCount);

            foreach (string providerId in providerIds)
            {
                ProviderSummary cachedProvider = allCachedProviders.FirstOrDefault(m => m.Id == providerId);
                
                if (cachedProvider != null)
                {
                    providerSummaries.Add(cachedProvider);
                }
            }

            await _cacheProvider.KeyDeleteAsync<ProviderSummary>(cacheKey);

            await _cacheProvider.CreateListAsync(providerSummaries, cacheKey);

            return new OkObjectResult(providerSummaries.Count());
        }

        public async Task<IActionResult> FetchCoreProviderData(string specificationId)
        {
            IEnumerable<ProviderSummary>  providerSummaries = await this.GetScopedProvidersBySpecification(specificationId);

            if(providerSummaries.IsNullOrEmpty())
            {
                return new NoContentResult();
            }

            return new OkObjectResult(providerSummaries);
        }

        public async Task<IActionResult> GetScopedProviderIds(string specificationId)
        {
            IEnumerable<string> providerIds = await this.GetScopedProviderIdsBySpecification(specificationId);

            if (providerIds.IsNullOrEmpty())
            {
                return new NoContentResult();
            }

            return new OkObjectResult(providerIds);
        }

        private Task<IEnumerable<string>> GetScopedProviderIdsBySpecification(string specificationId)
        {
            if (string.IsNullOrWhiteSpace(specificationId))
            {
                throw new ArgumentNullException(nameof(specificationId));
            }

            string url = $"{getScopedProviderIdsUrl}{specificationId}";

            return _resultsApiClient.GetAsync<IEnumerable<string>>(url);
        }

        private async Task<IEnumerable<ProviderSummary>> GetScopedProvidersBySpecification(string specificationId)
        {
            string url = string.Format(getSpecificationSummary, specificationId);

            SpecificationSummary spec = await _specificationsApiClient.GetAsync<SpecificationSummary>(url);

            if (string.IsNullOrWhiteSpace(spec?.ProviderVersionId))
            {
                return null;
            }

            ProviderVersion providerVersion = await _providerVersionService.GetProvidersByVersion(spec.ProviderVersionId);

            if (providerVersion == null)
            {
                return null;
            }

            int totalCount = providerVersion.Providers.Count();

            string cacheKeyAllProviderSummaryCount = $"{CacheKeys.AllProviderSummaryCount}{specificationId}";
            string currentProviderCount = await _cacheProvider.GetAsync<string>(cacheKeyAllProviderSummaryCount);

            string cacheKeyAllProviderSummaries = $"{CacheKeys.AllProviderSummaries}{specificationId}";
            long totalProviderListCount = await _cacheProvider.ListLengthAsync<ProviderSummary>(cacheKeyAllProviderSummaries);

            if (string.IsNullOrWhiteSpace(currentProviderCount) || int.Parse(currentProviderCount) != totalCount || totalProviderListCount != totalCount)
            {
                await _cacheProvider.KeyDeleteAsync<ProviderSummary>(cacheKeyAllProviderSummaries);
                IEnumerable<ProviderSummary> providerSummaries = providerVersion.Providers.Select(x => new ProviderSummary
                {
                    Name = x.Name,
                    Id = x.ProviderId,
                    ProviderProfileIdType = x.ProviderProfileIdType,
                    UKPRN = x.UKPRN,
                    URN = x.URN,
                    Authority = x.Authority,
                    UPIN = x.UPIN,
                    ProviderSubType = x.ProviderSubType,
                    EstablishmentNumber = x.EstablishmentNumber,
                    ProviderType = x.ProviderType,
                    DateOpened = x.DateOpened,
                    DateClosed = x.DateClosed,
                    LACode = x.LACode,
                    CrmAccountId = x.CrmAccountId,
                    LegalName = x.LegalName,
                    NavVendorNo = x.NavVendorNo,
                    DfeEstablishmentNumber = x.DfeEstablishmentNumber,
                    Status = x.Status,
                    PhaseOfEducation = x.PhaseOfEducation,
                    ReasonEstablishmentClosed = x.ReasonEstablishmentClosed,
                    ReasonEstablishmentOpened = x.ReasonEstablishmentOpened,
                    Successor = x.Successor,
                    TrustStatus = Enum.TryParse(x.TrustStatusViewModelString, out Models.Results.TrustStatus trustStatus) ? trustStatus : Models.Results.TrustStatus.NotApplicable,
                    TrustName = x.TrustName,
                    TrustCode = x.TrustCode
                });

                await _cacheProvider.CreateListAsync(providerSummaries, cacheKeyAllProviderSummaries);

                await _cacheProvider.SetAsync(cacheKeyAllProviderSummaryCount, totalCount.ToString(), TimeSpan.FromDays(365), true);
                return providerSummaries;
            }
            else
            {
                return await _cacheProvider.ListRangeAsync<ProviderSummary>(cacheKeyAllProviderSummaries, 0, totalCount);
            }
        }
    }
}
