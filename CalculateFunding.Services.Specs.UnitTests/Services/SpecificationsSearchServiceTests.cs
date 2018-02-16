﻿using CalculateFunding.Models;
using CalculateFunding.Models.Specs;
using CalculateFunding.Repositories.Common.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Search.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using NSubstitute;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Specs.Services
{
    [TestClass]
    public class SpecificationsSearchServiceTests
    {
        [TestMethod]
        public async Task SearchSpecifications_GivenNullSearchModel_ReturnsBadRequest()
        {
            //Arrange
            HttpRequest request = Substitute.For<HttpRequest>();

            ILogger logger = CreateLogger();

            SpecificationsSearchService service = CreateSearchService(logger: logger);

            //Act
            IActionResult result = await service.SearchSpecifications(request);

            //Assert
            logger
                .Received(1)
                .Error("A null or invalid search model was provided for searching specifications");

            result
                .Should()
                .BeOfType<BadRequestObjectResult>();
        }

        [TestMethod]
        public async Task SearchSpecifications_GivenInvalidPageNumber_ReturnsBadRequest()
        {
            //Arrange
            SearchModel model = new SearchModel
            {
                PageNumber = 0,
                Top = 1
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            SpecificationsSearchService service = CreateSearchService(logger: logger);

            //Act
            IActionResult result = await service.SearchSpecifications(request);

            //Assert
            logger
                .Received(1)
                .Error("A null or invalid search model was provided for searching specifications");

            result
                .Should()
                .BeOfType<BadRequestObjectResult>();
        }

        [TestMethod]
        public async Task SearchSpecifications_GivenInvalidTop_ReturnsBadRequest()
        {
            //Arrange
            SearchModel model = new SearchModel
            {
                PageNumber = 1,
                Top = 0
            };
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Body
                .Returns(stream);

            ILogger logger = CreateLogger();

            SpecificationsSearchService service = CreateSearchService(logger: logger);

            //Act
            IActionResult result = await service.SearchSpecifications(request);

            //Assert
            logger
                .Received(1)
                .Error("A null or invalid search model was provided for searching specifications");

            result
                .Should()
                .BeOfType<BadRequestObjectResult>();
        }

        [TestMethod]
        async public Task SearchSpecifications_GivenSearchThrowsException_ReturnsStatusCode500()
        {
            //Arrange
            SearchModel model = CreateSearchModel();
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Body
                .Returns(stream);

            ISearchRepository<SpecificationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .When(x => x.Search(Arg.Any<string>(), Arg.Any<SearchParameters>()))
                .Do(x => { throw new FailedToQuerySearchException(Arg.Any<string>(), Arg.Any<Exception>()); });

            ILogger logger = CreateLogger();

            SpecificationsSearchService service = CreateSearchService(searchRepository, logger);

            //Act
            IActionResult result = await service.SearchSpecifications(request);

            //Assert
            result
                .Should()
                .BeOfType<StatusCodeResult>();

            StatusCodeResult statusCodeResult = result as StatusCodeResult;

            statusCodeResult
                .StatusCode
                .Should()
                .Be(500);
        }

        [TestMethod]
        async public Task SearchSpecifications_GivenSearchReturnsResults_ReturnsOKResult()
        {
            //Arrange
            SearchModel model = CreateSearchModel();
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Body
                .Returns(stream);

            SearchResults<SpecificationIndex> searchResults = new SearchResults<SpecificationIndex>
            {
                TotalCount = 1 
            };

            ISearchRepository<SpecificationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .Search(Arg.Is("SearchTermTest"), Arg.Any<SearchParameters>())
                .Returns(searchResults);

            ILogger logger = CreateLogger();

            SpecificationsSearchService service = CreateSearchService(searchRepository, logger);

            //Act
            IActionResult result = await service.SearchSpecifications(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();
        }

        [TestMethod]
        async public Task SearchSpecifications_GivenSearchReturnsResultsAndDataDefinitionsIsNull_ReturnsOKResult()
        {
            //Arrange
            SearchModel model = CreateSearchModel();
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Body
                .Returns(stream);

            SearchResults<SpecificationIndex> searchResults = new SearchResults<SpecificationIndex>
            {
                TotalCount = 1,
                Results = new List<Repositories.Common.Search.SearchResult<SpecificationIndex>>
                {
                    new Repositories.Common.Search.SearchResult<SpecificationIndex>
                    {
                        Result = new SpecificationIndex
                        {
                            DataDefinitionRelationshipIds = null
                        }
                    }
                }
            };

            ISearchRepository<SpecificationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .Search(Arg.Is("SearchTermTest"), Arg.Any<SearchParameters>())
                .Returns(searchResults);

            ILogger logger = CreateLogger();

            SpecificationsSearchService service = CreateSearchService(searchRepository, logger);

            //Act
            IActionResult result = await service.SearchSpecifications(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult okObjectResult = result as OkObjectResult;

            SpecificationSearchResults specificationSearchResults = okObjectResult.Value as SpecificationSearchResults;

            specificationSearchResults
                .Results
                .Count()
                .Should()
                .Be(1);

            specificationSearchResults
                .TotalCount
                .Should()
                .Be(1);

            specificationSearchResults
               .Results
               .First()
               .DefinitionRelationshipCount
               .Should()
               .Be(0);
        }

        [TestMethod]
        async public Task SearchSpecifications_GivenSearchReturnsResultsAndDataDefinitionsHasItems_ReturnsOKResult()
        {
            //Arrange
            SearchModel model = CreateSearchModel();
            string json = JsonConvert.SerializeObject(model);
            byte[] byteArray = Encoding.UTF8.GetBytes(json);
            MemoryStream stream = new MemoryStream(byteArray);

            HttpRequest request = Substitute.For<HttpRequest>();
            request
                .Body
                .Returns(stream);

            SearchResults<SpecificationIndex> searchResults = new SearchResults<SpecificationIndex>
            {
                TotalCount = 1,
                Results = new List<Repositories.Common.Search.SearchResult<SpecificationIndex>>
                {
                    new Repositories.Common.Search.SearchResult<SpecificationIndex>
                    {
                        Result = new SpecificationIndex
                        {
                            DataDefinitionRelationshipIds = new[]{"def-1", "def-2"}
                        }
                    }
                }
            };

            ISearchRepository<SpecificationIndex> searchRepository = CreateSearchRepository();
            searchRepository
                .Search(Arg.Is("SearchTermTest"), Arg.Any<SearchParameters>())
                .Returns(searchResults);

            ILogger logger = CreateLogger();

            SpecificationsSearchService service = CreateSearchService(searchRepository, logger);

            //Act
            IActionResult result = await service.SearchSpecifications(request);

            //Assert
            result
                .Should()
                .BeOfType<OkObjectResult>();

            OkObjectResult okObjectResult = result as OkObjectResult;

            SpecificationSearchResults specificationSearchResults = okObjectResult.Value as SpecificationSearchResults;

            specificationSearchResults
                .Results
                .Count()
                .Should()
                .Be(1);

            specificationSearchResults
                .TotalCount
                .Should()
                .Be(1);

            specificationSearchResults
               .Results
               .First()
               .DefinitionRelationshipCount
               .Should()
               .Be(2);
        }


        static SpecificationsSearchService CreateSearchService(ISearchRepository<SpecificationIndex> searchRepository = null,
            ILogger logger = null)
        {
            return new SpecificationsSearchService(searchRepository ?? CreateSearchRepository(), logger ?? CreateLogger());
        }

        static ISearchRepository<SpecificationIndex> CreateSearchRepository()
        {
            return Substitute.For<ISearchRepository<SpecificationIndex>>();
        }

        static ILogger CreateLogger()
        {
            return Substitute.For<ILogger>();
        }

        static SearchModel CreateSearchModel()
        {
            return new SearchModel()
            {
                SearchTerm = "SearchTermTest",
                PageNumber = 1,
                Top = 20,
                Filters = new Dictionary<string, string[]>
                {
                    { "periodName" , new[]{"18/19" } }
                }
            };
        }
    }
}
