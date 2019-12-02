using System.Threading.Tasks;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Datasets.ViewModels;
using CalculateFunding.Services.Datasets.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CalculateFunding.Api.Datasets.Controllers
{
    public class DatasetsController : Controller
    {
        private readonly IDefinitionsService _definitionService;
        private readonly IDatasetService _datasetService;
        private readonly IDatasetSearchService _datasetSearchService;
        private readonly IDatasetDefinitionSearchService _datasetDefinitionSearchService;
        private readonly IDefinitionSpecificationRelationshipService _definitionSpecificationRelationshipService;
        private readonly IProcessDatasetService _processDatasetService;

        public DatasetsController(
            IDefinitionsService definitionService,
            IDatasetService datasetService,
            IDatasetSearchService datasetSearchService,
            IDatasetDefinitionSearchService datasetDefinitionSearchService,
            IDefinitionSpecificationRelationshipService definitionSpecificationRelationshipService,
            IProcessDatasetService processDatasetService)
        {
            Guard.ArgumentNotNull(definitionService, nameof(definitionService));
            Guard.ArgumentNotNull(datasetService, nameof(datasetService));
            Guard.ArgumentNotNull(datasetSearchService, nameof(datasetSearchService));
            Guard.ArgumentNotNull(datasetDefinitionSearchService, nameof(datasetDefinitionSearchService));
            Guard.ArgumentNotNull(definitionSpecificationRelationshipService, nameof(definitionSpecificationRelationshipService));
            Guard.ArgumentNotNull(processDatasetService, nameof(processDatasetService));

            _definitionService = definitionService;
            _datasetService = datasetService;
            _datasetSearchService = datasetSearchService;
            _datasetDefinitionSearchService = datasetDefinitionSearchService;
            _definitionSpecificationRelationshipService = definitionSpecificationRelationshipService;
            _processDatasetService = processDatasetService;
        }

        [Route("api/datasets/data-definitions")]
        [HttpPost]
        public Task<IActionResult> DataDefinitionSave()
        {
            return _definitionService.SaveDefinition(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-data-definitions")]
        [HttpGet]
        public Task<IActionResult> GetDatasetDefinitions()
        {
            return _definitionService.GetDatasetDefinitions(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-dataset-definition-by-id")]
        [HttpGet]
        public Task<IActionResult> GetDatasetDefinitionById()
        {
            return _definitionService.GetDatasetDefinitionById(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-dataset-definitions-by-ids")]
        [HttpGet]
        public Task<IActionResult> GetDatasetDefinitionsById()
        {
            return _definitionService.GetDatasetDefinitionsByIds(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/create-new-dataset")]
        [HttpPost]
        public Task<IActionResult> CreateDataset()
        {
            return _datasetService.CreateNewDataset(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/dataset-version-update")]
        [HttpPost]
        public Task<IActionResult> DatasetVersionUpdate()
        {
            return _datasetService.DatasetVersionUpdate(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/datasets-search")]
        [HttpPost]
        public Task<IActionResult> DatasetsSearch()
        {
            return _datasetSearchService.SearchDatasets(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/datasets-version-search")]
        [HttpPost]
        public Task<IActionResult> DatasetsVersionSearch()
        {
            return _datasetSearchService.SearchDatasetVersion(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/dataset-definitions-search")]
        [HttpPost]
        public Task<IActionResult> DatasetDefinitionsSearch()
        {
            return _datasetDefinitionSearchService.SearchDatasetDefinitions(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/validate-dataset")]
        [HttpPost]
        public Task<IActionResult> ValidateDataset()
        {
            return _datasetService.ValidateDataset(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/create-definitionspecification-relationship")]
        [HttpPost]
        public Task<IActionResult> CreateDefinitionSpecificationRelationship()
        {
            return _definitionSpecificationRelationshipService.CreateRelationship(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-definitions-relationships")]
        [HttpGet]
        public Task<IActionResult> GetDefinitionRelationships()
        {
            return _definitionSpecificationRelationshipService.GetRelationshipsBySpecificationId(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-definition-relationship-by-specificationid-name")]
        [HttpGet]
        public Task<IActionResult> GetDefinitionRelationshipBySpecificationIdAndName()
        {
            return _definitionSpecificationRelationshipService.GetRelationshipBySpecificationIdAndName(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-datasets-by-definitionid")]
        [HttpGet]
        public Task<IActionResult> GetDatasetsByDefinitionId()
        {
            return _datasetService.GetDatasetsByDefinitionId(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-relationships-by-specificationId")]
        [HttpGet]
        public Task<IActionResult> GetRealtionshipsBySpecificationId()
        {
            return _definitionSpecificationRelationshipService.GetCurrentRelationshipsBySpecificationId(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-datasources-by-relationshipid")]
        [HttpGet]
        public Task<IActionResult> GetDataSourcesByRelationshipId()
        {
            return _definitionSpecificationRelationshipService.GetDataSourcesByRelationshipId(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/assign-datasource-to-relationship")]
        [HttpPost]
        public Task<IActionResult> AssignDatasourceVersionToRelationship()
        {
            return _definitionSpecificationRelationshipService.AssignDatasourceVersionToRelationship(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/download-dataset-file")]
        [HttpGet]
        public Task<IActionResult> DownloadDatasetFile()
        {return _datasetService.DownloadDatasetFile(ControllerContext.HttpContext.Request);
        }

        [HttpPost("api/datasets/upload-dataset-file/{filename}")]
        [DisableRequestSizeLimitAttribute()]
        public async Task<IActionResult> UploadDatasetFile([FromRoute]string filename, [FromBody]DatasetMetadataViewModel datasetMetadataViewModel)
        {
            return await _datasetService.UploadDatasetFile(filename, datasetMetadataViewModel);
        }

        [Route("api/datasets/reindex")]
        [HttpGet]
        public Task<IActionResult> ReindexDatasets()
        {
            return _datasetService.Reindex(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasetsversions/reindex")]
        [HttpGet]
        public Task<IActionResult> ReindexDatasetsVersions()
        {
            return _datasetService.ReindexDatasetVersions(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-currentdatasetversion-by-datasetid")]
        [HttpGet]
        public Task<IActionResult> GetCurrentDatasetVersionByDatasetId()
        {
            return _datasetService.GetCurrentDatasetVersionByDatasetId(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-schema-download-url")]
        [HttpPost]
        public Task<IActionResult> GetDatasetSchemaSasUrl()
        {
            return _definitionService.GetDatasetSchemaSasUrl(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/regenerate-providersourcedatasets")]
        [HttpPost]
        public Task<IActionResult> RegenerateProviderSourceDatasets()
        {
            return _datasetService.RegenerateProviderSourceDatasets(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/get-dataset-validate-status")]
        [HttpGet]
        public Task<IActionResult> GetValidateDatasetStatus()
        {
            return _datasetService.GetValidateDatasetStatus(ControllerContext.HttpContext.Request);
        }

        [Route("api/datasets/{specificationId}/datasetAggregations")]
        [HttpGet]
        public async Task<IActionResult> GetDatasetAggregations(string specificationId)
        {
            if (string.IsNullOrWhiteSpace(specificationId))
            {
                return new BadRequestObjectResult("Misssing specification id");
            }

            return await _processDatasetService.GetDatasetAggregationsBySpecificationId(specificationId);
        }

        [Route("api/datasets/{specificationId}/schemaRelationshipFields")]
        [HttpGet]
        public async Task<IActionResult> GetSchemaRelationshipsBySpecificationId(string specificationId)
        {
            if (string.IsNullOrWhiteSpace(specificationId))
            {
                return new BadRequestObjectResult("Misssing specification id");
            }

            return await _definitionSpecificationRelationshipService.GetCurrentDatasetRelationshipFieldsBySpecificationId(specificationId);
        }

        [Route("api/datasets/{datasetDefinitionId}/relationshipSpecificationIds")]
        [HttpGet]
        public async Task<IActionResult> GetSpecificationIdsForRelationshipDefinitionId(string datasetDefinitionId)
        {
            if (string.IsNullOrWhiteSpace(datasetDefinitionId))
            {
                return new BadRequestObjectResult($"Misssing {nameof(datasetDefinitionId)}");
            }

            return await _definitionSpecificationRelationshipService.GetSpecificationIdsForRelationshipDefinitionId(datasetDefinitionId);
        }

        [Route("api/datasets/{specificationId}/{datasetDefinitionId}/relationships")]
        [HttpGet]
        public async Task<IActionResult> GetRelationshipsBySpecificationIdDasetDefinitionId(string specificationId, string datasetDefinitionId)
        {
            if (string.IsNullOrWhiteSpace(specificationId))
            {
                return new BadRequestObjectResult("Misssing specification id");
            }

            if (string.IsNullOrWhiteSpace(datasetDefinitionId))
            {
                return new BadRequestObjectResult("Misssing dataset definition id");
            }

            return await _definitionSpecificationRelationshipService.GetCurrentRelationshipsBySpecificationIdAndDatasetDefinitionId(specificationId, datasetDefinitionId);
        }
    }
}
