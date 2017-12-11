﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Models;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

namespace CalculateFunding.Repositories.Common.Search
{
    public class SearchRepositorySettings
    {
        public string SearchServiceName { get; set; }
        public string SearchKey { get; set; }

    }
    public class SearchRepository<T> where T : class
    {
        private ISearchIndexClient _searchIndexClient;

        private static readonly SearchParameters DefaultParameters = new SearchParameters {IncludeTotalResultCount = true};
        private readonly SearchRepositorySettings _settings;
        private readonly SearchServiceClient _searchServiceClient;
        private readonly string _indexName;

        public SearchRepository(SearchRepositorySettings settings)
        {
            _indexName = typeof(T).Name.ToLowerInvariant();
            _settings = settings;
            _searchServiceClient = new SearchServiceClient(_settings.SearchServiceName, new SearchCredentials(_settings.SearchKey));
        }

        public async Task<ISearchIndexClient> GetOrCreateIndex()
        {
            if (_searchIndexClient == null)
            {
                if (!await _searchServiceClient.Indexes.ExistsAsync(_indexName))
                {
                    await Initialize();
                }
                _searchIndexClient = _searchServiceClient.Indexes.GetClient(_indexName);
            }

            return _searchIndexClient;
        }

        public async Task Initialize()
        {
            var searchInitializer = new SearchInitializer(_settings.SearchServiceName, _settings.SearchKey, null);
            await searchInitializer.Initialise<T>();
        }

        public async Task<SearchResults<T>> Search(string searchTerm, SearchParameters searchParameters = null)
        {
            var client = await GetOrCreateIndex();
            var azureSearchResult = await client.Documents.SearchAsync<T>(searchTerm, searchParameters ?? DefaultParameters);

            var response = new SearchResults<T>
            {
                SearchTerm = searchTerm,
                TotalCount = azureSearchResult.Count,
                Facets = azureSearchResult.Facets.Select(x => new Facet
                {
                }).ToList(),
                Results = azureSearchResult.Results.Select(x => new SearchResult<T>
                {
                    HitHighLights = x.Highlights,
                    Result = x.Document,
                    Score = x.Score
                }).ToList()
            };
            return response;

        }


        public async Task<IList<IndexError>> Index(IList<T> documents)
        {
            var client = await GetOrCreateIndex();
            var errors = new List<IndexError>();

            foreach (var batch in documents.ToBatches(1000))
            {
                var indexResult = await client.Documents.IndexAsync(new IndexBatch<T>(batch.Select(IndexAction.MergeOrUpload)));
                foreach (var result in indexResult.Results)
                {
                    if (!result.Succeeded)
                    {
                        errors.Add(new IndexError {Key = result.Key, ErrorMessage = result.ErrorMessage});
                    }
                }
            }

            return errors;

        }
    }


}
