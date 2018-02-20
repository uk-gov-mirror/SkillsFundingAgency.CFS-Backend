﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Models;
using CalculateFunding.Models.Datasets;
using CalculateFunding.Repositories.Common.Search;
using CalculateFunding.Repositories.Common.Search.Results;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Datasets.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Search.Models;
using Newtonsoft.Json;
using Serilog;

namespace CalculateFunding.Services.Results
{
    public class ResultsSearchService : IResultsSearchService
    {
        private readonly ILogger _logger;
        private readonly ISearchRepository<ProviderIndex> _searchRepository;

        private FacetFilterType[] Facets = {
            new FacetFilterType("periodNames", true),
            new FacetFilterType("status"),
            new FacetFilterType("definitionName"),
            new FacetFilterType("specificationNames", true),
        };

        private IEnumerable<string> DefaultOrderBy = new[] { "lastUpdatedDate desc" };

        private ProviderSearchResults results = new ProviderSearchResults();

        public ResultsSearchService(ILogger logger,
            ISearchRepository<ProviderIndex> searchRepository)
        {
            Guard.ArgumentNotNull(searchRepository, nameof(searchRepository));
            Guard.ArgumentNotNull(logger, nameof(logger));

            _logger = logger;
            _searchRepository = searchRepository;
        }

        async public Task<IActionResult> SearchDatasets(HttpRequest request)
        {
            string json = await request.GetRawBodyStringAsync();

            SearchModel searchModel = JsonConvert.DeserializeObject<SearchModel>(json);

            if (searchModel == null || searchModel.PageNumber < 1 || searchModel.Top < 1)
            {
                _logger.Error("A null or invalid search model was provided for searching datasets");

                return new BadRequestObjectResult("An invalid search model was provided");
            }

            IEnumerable<Task<SearchResults<ProviderIndex>>> searchTasks = BuildSearchTasks(searchModel);

            try
            {
                await TaskHelpers.WhenAllAndThrow(searchTasks.ToArraySafe());

                foreach (var searchTask in searchTasks)
                    ProcessSearchResults(searchTask.Result, searchModel);

                return new OkObjectResult(results);
            }
            catch (FailedToQuerySearchException exception)
            {
                _logger.Error(exception, $"Failed to query search with term: {searchModel.SearchTerm}");

                return new StatusCodeResult(500);
            }
        }

	    public Task<IActionResult> GetProviderResults(HttpRequest httpContextRequest)
	    {
		    throw new System.NotImplementedException();
	    }

	    IDictionary<string, string> BuildFacetDictionary(SearchModel searchModel)
        {
            if (searchModel.Filters == null)
                searchModel.Filters = new Dictionary<string, string[]>();

            searchModel.Filters = searchModel.Filters.ToList().Where(m => !m.Value.IsNullOrEmpty())
                .ToDictionary(m => m.Key, m => m.Value);

            IDictionary<string, string> facetDictionary = new Dictionary<string, string>();

            foreach (var facet in Facets)
            {
                string filter = "";
                if (searchModel.Filters.ContainsKey(facet.Name) && searchModel.Filters[facet.Name].AnyWithNullCheck())
                {
                    if (facet.IsMulti)
                        filter = $"({facet.Name}/any(x: {string.Join(" or ", searchModel.Filters[facet.Name].Select(x => $"x eq '{x}'"))}))";
                    else
                        filter = $"({string.Join(" or ", searchModel.Filters[facet.Name].Select(x => $"{facet.Name} eq '{x}'"))})";
                }
                facetDictionary.Add(facet.Name, filter);
            }

            return facetDictionary;
        }

        IEnumerable<Task<SearchResults<ProviderIndex>>> BuildSearchTasks(SearchModel searchModel)
        {
            IDictionary<string, string> facetDictionary = BuildFacetDictionary(searchModel);

            IEnumerable<Task<SearchResults<ProviderIndex>>> searchTasks = new Task<SearchResults<ProviderIndex>>[0];

            if (searchModel.IncludeFacets)
            {
                foreach (var filterPair in facetDictionary)
                {
                    searchTasks = searchTasks.Concat(new[]
                    {
                        Task.Run(() =>
                        {
                            var s = facetDictionary.Where(x => x.Key != filterPair.Key && !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value);

                            return _searchRepository.Search(searchModel.SearchTerm, new SearchParameters
                            {
                                Facets = new[]{ filterPair.Key },
                                SearchMode = SearchMode.Any,
                                IncludeTotalResultCount = true,
                                Filter = string.Join(" and ", facetDictionary.Where(x => x.Key != filterPair.Key && !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value)),
                                QueryType = QueryType.Full
                            });
                        })
                    });
                }
            }

            searchTasks = searchTasks.Concat(new[]
            {
                BuildItemsSearchTask(facetDictionary, searchModel)
            });

            return searchTasks;
        }

		//TODO - refactor - seems common?
        Task<SearchResults<ProviderIndex>> BuildItemsSearchTask(IDictionary<string, string> facetDictionary, SearchModel searchModel)
        {
            int skip = (searchModel.PageNumber - 1) * searchModel.Top;
            return Task.Run(() =>
            {
                return _searchRepository.Search(searchModel.SearchTerm, new SearchParameters
                {
                    Skip = skip,
                    Top = searchModel.Top,
                    SearchMode = SearchMode.Any,
                    IncludeTotalResultCount = true,
                    Filter = string.Join(" and ", facetDictionary.Values.Where(x => !string.IsNullOrWhiteSpace(x))),
                    OrderBy = searchModel.OrderBy.IsNullOrEmpty() ? DefaultOrderBy.ToList() : searchModel.OrderBy.ToList(),
                    QueryType = QueryType.Full
                });
            });
        }

		void ProcessSearchResults(SearchResults<ProviderIndex> searchResult, SearchModel searchModel)
        {
            if (!searchResult.Facets.IsNullOrEmpty())
            {
                results.Facets = results.Facets.Concat(searchResult.Facets);
            }
            else
            {
                results.TotalCount = (int)(searchResult?.TotalCount ?? 0);
                results.Results = searchResult?.Results?.Select(m => new ProviderSearchResult
                {
                    //Id = m.Result.Id,
                    //Name = m.Result.Name,
                    //Status = m.Result.Status,
                    //DefinitionName = m.Result.DefinitionName,
                    //LastUpdatedDate = m.Result.LastUpdatedDate,
                    //PeriodNames = m.Result.PeriodNames,
                    //SpecificationNames = m.Result.SpecificationNames,
                });
            }
        }
    }
}
