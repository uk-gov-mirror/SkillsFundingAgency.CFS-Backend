﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CalculateFunding.Common.CosmosDb;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Results;
using CalculateFunding.Services.CalcEngine.Interfaces;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Core.Options;

namespace CalculateFunding.Services.CalcEngine
{
    public class ProviderSourceDatasetsRepository : IProviderSourceDatasetsRepository
    {
        private readonly CosmosRepository _cosmosRepository;
        private readonly EngineSettings _engineSettings;

        public ProviderSourceDatasetsRepository(CosmosRepository cosmosRepository, EngineSettings engineSettings)
        {
            _cosmosRepository = cosmosRepository;
            _engineSettings = engineSettings;
        }

        public async Task<IEnumerable<ProviderSourceDataset>> GetProviderSourceDatasetsByProviderIdsAndSpecificationId(IEnumerable<string> providerIds, string specificationId)
        {
            if (providerIds.IsNullOrEmpty())
            {
                return Enumerable.Empty<ProviderSourceDataset>();
            }

            Guard.IsNullOrWhiteSpace(specificationId, nameof(specificationId));

            ConcurrentBag<ProviderSourceDataset> results = new ConcurrentBag<ProviderSourceDataset>();

            List<Task> allTasks = new List<Task>();
            SemaphoreSlim throttler = new SemaphoreSlim(initialCount: _engineSettings.GetProviderSourceDatasetsDegreeOfParallelism);
            foreach (string providerId in providerIds)
            {
                await throttler.WaitAsync();
                allTasks.Add(
                    Task.Run(async () =>
                    {
                        try
                        {
                            CosmosDbQuery cosmosDbQuery = new CosmosDbQuery
                            {
                                QueryText = @"SELECT     *
                                            FROM    Root r 
                                            WHERE   r.documentType = @DocumentType
                                                    AND r.content.specificationId = @SpecificationId 
                                                    AND r.deleted = false",
                                Parameters = new[]
                                {
                                    new CosmosDbQueryParameter("@DocumentType", nameof(ProviderSourceDataset)),
                                    new CosmosDbQueryParameter("@SpecificationId", specificationId)
                                }
                            };

                            IEnumerable<ProviderSourceDataset> providerSourceDatasetResults =
                                await _cosmosRepository.QueryPartitionedEntity<ProviderSourceDataset>(cosmosDbQuery, partitionKey: providerId);

                            foreach (ProviderSourceDataset repoResult in providerSourceDatasetResults)
                            {
                                results.Add(repoResult);
                            }
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }));
            }
            await TaskHelper.WhenAllAndThrow(allTasks.ToArray());

            return results.AsEnumerable();
        }
    }
}
