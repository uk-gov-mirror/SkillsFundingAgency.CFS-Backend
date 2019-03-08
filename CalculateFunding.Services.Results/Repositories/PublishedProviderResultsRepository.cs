﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CalculateFunding.Common.CosmosDb;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.Models.HealthCheck;
using CalculateFunding.Models.Results;
using CalculateFunding.Models.Specs;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Results.Interfaces;
using LinqKit;
using Newtonsoft.Json.Linq;

namespace CalculateFunding.Services.Results.Repositories
{
    public class PublishedProviderResultsRepository : IPublishedProviderResultsRepository, IHealthChecker
    {
        private readonly CosmosRepository _cosmosRepository;

        public PublishedProviderResultsRepository(CosmosRepository cosmosRepository)
        {
            _cosmosRepository = cosmosRepository;
        }

        public async Task<ServiceHealth> IsHealthOk()
        {
            (bool Ok, string Message) cosmosRepoHealth = await _cosmosRepository.IsHealthOk();

            ServiceHealth health = new ServiceHealth()
            {
                Name = nameof(PublishedProviderResultsRepository)
            };
            health.Dependencies.Add(new DependencyHealth { HealthOk = cosmosRepoHealth.Ok, DependencyName = _cosmosRepository.GetType().GetFriendlyName(), Message = cosmosRepoHealth.Message });

            return health;
        }

        public Task SavePublishedResults(IEnumerable<PublishedProviderResult> publishedResults)
        {
            Guard.ArgumentNotNull(publishedResults, nameof(publishedResults));

            return _cosmosRepository.BulkCreateAsync<PublishedProviderResult>(publishedResults.Select(m => new KeyValuePair<string, PublishedProviderResult>(m.ProviderId, m)), degreeOfParallelism: 25);
        }

        public Task<IEnumerable<PublishedProviderResult>> GetPublishedProviderResultsForSpecificationId(string specificationId)
        {
            IQueryable<PublishedProviderResult> cosmosResults = _cosmosRepository.Query<PublishedProviderResult>(enableCrossPartitionQuery: true).Where(m => m.SpecificationId == specificationId);

            List<PublishedProviderResult> result = new List<PublishedProviderResult>(cosmosResults);
            return Task.FromResult(result.AsEnumerable());
        }

        public async Task<IEnumerable<PublishedProviderResult>> GetPublishedProviderResultsForSpecificationIdAndProviderId(string specificationId, IEnumerable<string> providerIds)
        {
            ConcurrentBag<PublishedProviderResult> results = new ConcurrentBag<PublishedProviderResult>();

            List<Task> allTasks = new List<Task>();
            SemaphoreSlim throttler = new SemaphoreSlim(initialCount: 15);
            foreach (string providerId in providerIds)
            {
                await throttler.WaitAsync();
                allTasks.Add(
                    Task.Run(async () =>
                    {
                        try
                        {
                            string sql = $"select * from root c where c.content.specificationId = \"{specificationId}\" and c.documentType = \"PublishedProviderResult\" and c.content.providerId = \"{providerId}\" and c.deleted = false";

                            IEnumerable<PublishedProviderResult> publishedProviderResults = await _cosmosRepository.QueryPartitionedEntity<PublishedProviderResult>(sql, partitionEntityId: providerId);
                            foreach (PublishedProviderResult result in publishedProviderResults)
                            {
                                results.Add(result);
                            }
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }));
            }
            await TaskHelper.WhenAllAndThrow(allTasks.ToArray());

            return results;
        }

        public async Task<IEnumerable<Migration.PublishedProviderResult>> GetPublishedProviderResultsForSpecificationIdAndProviderIdMigrationOnly(string specificationId, IEnumerable<string> providerIds)
        {
            ConcurrentBag<Migration.PublishedProviderResult> results = new ConcurrentBag<Migration.PublishedProviderResult>();

            List<Task> allTasks = new List<Task>();
            SemaphoreSlim throttler = new SemaphoreSlim(initialCount: 15);
            foreach (string providerId in providerIds)
            {
                await throttler.WaitAsync();
                allTasks.Add(
                    Task.Run(async () =>
                    {
                        try
                        {
                            string sql = $"select * from root c where c.content.specificationId = \"{specificationId}\" and c.documentType = \"PublishedProviderResult\" and c.content.providerId = \"{providerId}\" and c.deleted = false";

                            IEnumerable<Migration.PublishedProviderResult> publishedProviderResults = await _cosmosRepository.QueryPartitionedEntity<Migration.PublishedProviderResult>(sql, partitionEntityId: providerId);
                            foreach (Migration.PublishedProviderResult result in publishedProviderResults)
                            {
                                results.Add(result);
                            }
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }));
            }
            await TaskHelper.WhenAllAndThrow(allTasks.ToArray());

            return results;
        }

        public async Task<IEnumerable<PublishedProviderResultExisting>> GetExistingPublishedProviderResultsForSpecificationId(string specificationId)
        {
            string query = "SELECT r.id, r.updatedAt, r.content.providerId," +
                "r.content.fundingStreamResult.allocationLineResult.current[\"value\"], " +
                "r.content.fundingStreamResult.allocationLineResult.allocationLine.id as allocationLineId, " +
                "r.content.fundingStreamResult.allocationLineResult.current.major as major, " +
                "r.content.fundingStreamResult.allocationLineResult.current.minor as minor, " +
                "r.content.fundingStreamResult.allocationLineResult.current.version as version, " +
                "r.content.fundingStreamResult.allocationLineResult.current.status as status, " +
                "r.content.fundingStreamResult.allocationLineResult.published as published, " +
                "r.content.fundingStreamResult.allocationLineResult.current.profilePeriods, " +
                "r.content.fundingStreamResult.allocationLineResult.current.financialEnvelopes, " +
                "r.content.fundingStreamResult.allocationLineResult.allocationLine.providerLookups, " +
                "r.content.fundingStreamResult.allocationLineResult.hasResultBeenVaried, " +
                "r.content.fundingStreamResult.allocationLineResult.current.provider " +
                "FROM Root r where r.documentType = 'PublishedProviderResult' " +
                "and r.deleted = false " +
                "and r.content.specificationId = '" + specificationId + "'";

            IEnumerable<dynamic> existingResults = await _cosmosRepository.QueryDynamic<dynamic>(query, true, 1000);

            List<PublishedProviderResultExisting> results = new List<PublishedProviderResultExisting>();
            foreach (dynamic existingResult in existingResults)
            {
                PublishedProviderResultExisting result = new PublishedProviderResultExisting()
                {
                    AllocationLineId = existingResult.allocationLineId,
                    Id = existingResult.id,
                    ProviderId = existingResult.providerId,
                    Value = existingResult.value != null ? Convert.ToDecimal(existingResult.value) : null,
                    Minor = DynamicExtensions.PropertyExists(existingResult, "minor") ? (int)existingResult.minor : 0,
                    Major = DynamicExtensions.PropertyExists(existingResult, "major") ? (int)existingResult.major : 0,
                    UpdatedAt = (DateTimeOffset?)existingResult.updatedAt,
                    Version = DynamicExtensions.PropertyExists(existingResult, "version") ? (int)existingResult.version : 0,
                    Published = DynamicExtensions.PropertyExistsAndIsNotNull(existingResult, "published") ? ((JObject)existingResult.published).ToObject<PublishedAllocationLineResultVersion>() : null,
                    HasResultBeenVaried = DynamicExtensions.PropertyExists(existingResult, "hasResultBeenVaried") ? (bool)existingResult.hasResultBeenVaried : false,
                    Provider = DynamicExtensions.PropertyExistsAndIsNotNull(existingResult, "provider") ? ((JObject)existingResult.provider).ToObject<ProviderSummary>() : null
                };

                result.Status = Enum.Parse(typeof(AllocationLineStatus), existingResult.status);

                result.ProfilePeriods = DynamicExtensions.PropertyExistsAndIsNotNull(existingResult, "profilePeriods") ? ((JArray)existingResult.profilePeriods).ToObject<List<ProfilingPeriod>>() : Enumerable.Empty<ProfilingPeriod>();
                result.ProviderLookups = DynamicExtensions.PropertyExistsAndIsNotNull(existingResult, "providerLookups") ? ((JArray)existingResult.providerLookups).ToObject<List<ProviderLookup>>() : Enumerable.Empty<ProviderLookup>();
                result.FinancialEnvelopes = DynamicExtensions.PropertyExistsAndIsNotNull(existingResult, "financialEnvelopes") ? ((JArray)existingResult.financialEnvelopes).ToObject<List<FinancialEnvelope>>() : Enumerable.Empty<FinancialEnvelope>();
                
                results.Add(result);
            }

            return results;
        }

        public Task<IEnumerable<PublishedProviderResult>> GetPublishedProviderResultsByFundingPeriodIdAndSpecificationIdAndFundingStreamId(string fundingPeriod, string specificationId, string fundingStreamId)
        {
            IQueryable<PublishedProviderResult> results = _cosmosRepository.Query<PublishedProviderResult>(enableCrossPartitionQuery: true).Where(m => m.SpecificationId == specificationId && m.FundingPeriod.Id == fundingPeriod && m.FundingStreamResult.FundingStream.Id == fundingStreamId);

            return Task.FromResult(results.AsEnumerable());
        }

        public Task<IEnumerable<PublishedProviderResultByAllocationLineViewModel>> GetPublishedProviderResultsSummaryByFundingPeriodIdAndSpecificationIdAndFundingStreamId(string fundingPeriodId, string specificationId, string fundingStreamId)
        {
            Guard.IsNullOrWhiteSpace(specificationId, nameof(specificationId));
            Guard.IsNullOrWhiteSpace(fundingStreamId, nameof(fundingStreamId));
            Guard.IsNullOrWhiteSpace(fundingPeriodId, nameof(fundingPeriodId));

            string query = @"SELECT r.content.specificationId,
r.content.fundingStreamResult.allocationLineResult.current.provider.name AS providerName,
r.content.providerId AS providerId,
r.content.fundingStreamResult.allocationLineResult.current.provider.providerType AS providerType,
r.content.fundingStreamResult.allocationLineResult.current.provider.ukPrn AS ukprn,
r.content.fundingStreamResult.fundingStream.name AS fundingStreamName,
r.content.fundingStreamResult.fundingStream.id AS fundingStreamId,
r.content.fundingStreamResult.allocationLineResult.allocationLine.id AS allocationLineId,
r.content.fundingStreamResult.allocationLineResult.allocationLine.name AS allocationLineName,
r.content.fundingStreamResult.allocationLineResult.current[""value""] AS fundingAmount,
r.content.fundingStreamResult.allocationLineResult.current.status AS status,
r.content.fundingStreamResult.allocationLineResult.current.date AS lastUpdated,
r.content.fundingStreamResult.allocationLineResult.current.provider.authority AS authority,
r.content.fundingStreamResult.allocationLineResult.current.versionNumber AS versionNumber
FROM Root r WHERE r.documentType = 'PublishedProviderResult'
AND r.content.specificationId = '" + specificationId + "' AND r.content.fundingPeriod.id = '" + fundingPeriodId + "' AND r.content.fundingStreamResult.fundingStream.id = '" + fundingStreamId + "'";

            IQueryable<PublishedProviderResultByAllocationLineViewModel> cosmosQuery = _cosmosRepository.RawQuery<PublishedProviderResultByAllocationLineViewModel>(query, enableCrossPartitionQuery: true);

            List<PublishedProviderResultByAllocationLineViewModel> result = new List<PublishedProviderResultByAllocationLineViewModel>(cosmosQuery);
            return Task.FromResult(result.AsEnumerable());
        }

        public Task<IEnumerable<PublishedProviderResultByAllocationLineViewModel>> GetPublishedProviderResultSummaryForSpecificationId(string specificationId)
        {
            Guard.IsNullOrWhiteSpace(specificationId, nameof(specificationId));

            string query = @"SELECT r.content.specificationId,
r.content.fundingStreamResult.allocationLineResult.current.provider.name AS providerName,
r.content.providerId AS providerId,
r.content.fundingStreamResult.allocationLineResult.current.provider.providerType AS providerType,
r.content.fundingStreamResult.allocationLineResult.current.provider.ukPrn AS ukprn,
r.content.fundingStreamResult.fundingStream.name AS fundingStreamName,
r.content.fundingStreamResult.fundingStream.id AS fundingStreamId,
r.content.fundingStreamResult.allocationLineResult.allocationLine.id AS allocationLineId,
r.content.fundingStreamResult.allocationLineResult.allocationLine.name AS allocationLineName,
r.content.fundingStreamResult.allocationLineResult.current[""value""] AS fundingAmount,
r.content.fundingStreamResult.allocationLineResult.current.status AS status,
r.content.fundingStreamResult.allocationLineResult.current.date AS lastUpdated,
r.content.fundingStreamResult.allocationLineResult.current.provider.authority AS authority,
r.content.fundingStreamResult.allocationLineResult.current.versionNumber AS versionNumber
FROM Root r WHERE r.documentType = 'PublishedProviderResult'
AND r.content.specificationId = '" + specificationId + "'";

            IQueryable<PublishedProviderResultByAllocationLineViewModel> cosmosQuery = _cosmosRepository.RawQuery<PublishedProviderResultByAllocationLineViewModel>(query, enableCrossPartitionQuery: true);
            List<PublishedProviderResultByAllocationLineViewModel> result = new List<PublishedProviderResultByAllocationLineViewModel>(cosmosQuery);
            return Task.FromResult(result.AsEnumerable());
        }

        public Task<IEnumerable<PublishedProviderResult>> GetPublishedProviderResultsForSpecificationAndStatus(string specificationId, UpdatePublishedAllocationLineResultStatusModel filterCriteria)
        {
            IQueryable<PublishedProviderResult> results = _cosmosRepository.Query<PublishedProviderResult>(enableCrossPartitionQuery: true).Where(m => m.SpecificationId == specificationId && m.FundingStreamResult.AllocationLineResult.Current.Status == filterCriteria.Status);

            ExpressionStarter<PublishedProviderResult> providerPredicate = PredicateBuilder.New<PublishedProviderResult>(false);

            foreach (UpdatePublishedAllocationLineResultStatusProviderModel provider in filterCriteria.Providers)
            {
                string providerId = provider.ProviderId;
                providerPredicate = providerPredicate.Or(p => p.ProviderId == providerId);

                ExpressionStarter<PublishedProviderResult> allocationLinePredicate = PredicateBuilder.New<PublishedProviderResult>(false);
                foreach (string allocationLineId in provider.AllocationLineIds)
                {
                    string temp = allocationLineId;
                    allocationLinePredicate = allocationLinePredicate.Or(a => a.FundingStreamResult.AllocationLineResult.AllocationLine.Id == temp);
                }

                providerPredicate = providerPredicate.And(allocationLinePredicate);
            }

            results = results.AsExpandable().Where(providerPredicate);
            List<PublishedProviderResult> result = new List<PublishedProviderResult>(results);
            return Task.FromResult(result.AsEnumerable());
        }

        public PublishedProviderResult GetPublishedProviderResultForId(string publishedProviderResultId)
        {
            Guard.IsNullOrWhiteSpace(publishedProviderResultId, nameof(publishedProviderResultId));

            IQueryable<PublishedProviderResult> results = _cosmosRepository.Query<PublishedProviderResult>(enableCrossPartitionQuery: true).Where(m => m.Id == publishedProviderResultId);

            return results.AsEnumerable().FirstOrDefault();
        }

        public async Task<PublishedProviderResult> GetPublishedProviderResultForId(string publishedProviderResultId, string providerId)
        {
            Guard.IsNullOrWhiteSpace(publishedProviderResultId, nameof(publishedProviderResultId));
            Guard.IsNullOrWhiteSpace(providerId, nameof(providerId));

            IEnumerable<PublishedProviderResult> results = await _cosmosRepository.QueryPartitionedEntity<PublishedProviderResult>($"SELECT * FROM Root r WHERE r.id ='{publishedProviderResultId}'", 1, providerId);

            return results.FirstOrDefault();
        }

        public Task<PublishedProviderResult> GetPublishedProviderResultForIdInPublishedState(string id)
        {
            Guard.IsNullOrWhiteSpace(id, nameof(id));

            IQueryable<PublishedProviderResult> results = _cosmosRepository.Query<PublishedProviderResult>(enableCrossPartitionQuery: true).Where(m => m.Id == id &&
                m.FundingStreamResult.AllocationLineResult.Current.Status == AllocationLineStatus.Published);

            return Task.FromResult(results.AsEnumerable().FirstOrDefault());
        }

        public PublishedAllocationLineResultVersion GetPublishedProviderResultVersionForFeedIndexId(string feedIndexId)
        {
            Guard.IsNullOrWhiteSpace(feedIndexId, nameof(feedIndexId));

            IQueryable<PublishedAllocationLineResultVersion> results = _cosmosRepository.Query<PublishedAllocationLineResultVersion>(enableCrossPartitionQuery: true).Where(m =>
                m.FeedIndexId == feedIndexId);

            return results.AsEnumerable().FirstOrDefault();
        }

        public async Task<IEnumerable<PublishedProviderResult>> GetAllNonHeldPublishedProviderResults()
        {
            IEnumerable<DocumentEntity<PublishedProviderResult>> documentEntities = await _cosmosRepository.GetAllDocumentsAsync<PublishedProviderResult>(
                query: m => m.Content.FundingStreamResult.AllocationLineResult.Current.Status != AllocationLineStatus.Held, enableCrossPartitionQuery: true);

            return documentEntities.Select(m => m.Content);
        }
    }
}
