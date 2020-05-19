﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models;
using CalculateFunding.Models.Aggregations;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Code;
using CalculateFunding.Models.Versioning;
using CalculateFunding.Repositories.Common.Search.Results;
using CalculateFunding.Services.Calcs.Interfaces;
using CalculateFunding.Services.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CalculateFunding.Api.Calcs.Controllers
{
    public class CalculationsController : ControllerBase
    {
        private readonly ICalculationService _calcsService;
        private readonly IPreviewService _previewService;
        private readonly ICalculationsSearchService _calcsSearchService;
        private readonly IBuildProjectsService _buildProjectsService;
        private readonly IQueueReIndexSpecificationCalculationRelationships _calculationRelationships;

        public CalculationsController(
            ICalculationService calcsService,
            ICalculationsSearchService calcsSearchService,
            IPreviewService previewService,
            IBuildProjectsService buildProjectsService, 
            IQueueReIndexSpecificationCalculationRelationships calculationRelationships)
        {
            Guard.ArgumentNotNull(calcsService, nameof(calcsService));
            Guard.ArgumentNotNull(calcsSearchService, nameof(calcsSearchService));
            Guard.ArgumentNotNull(previewService, nameof(previewService));
            Guard.ArgumentNotNull(buildProjectsService, nameof(buildProjectsService));
            Guard.ArgumentNotNull(calculationRelationships, nameof(calculationRelationships));

            _calcsService = calcsService;
            _previewService = previewService;
            _calcsSearchService = calcsSearchService;
            _buildProjectsService = buildProjectsService;
            _calculationRelationships = calculationRelationships;
        }
       
        [HttpGet("api/specifications/{specificationId}/calculations/calculationType/{calculationType}")]
        [Produces(typeof(CalculationSearchResults))]
        public async Task<IActionResult> SearchCalculationsForSpecification([FromRoute] string specificationId,
            [FromRoute] CalculationType calculationType,
            [FromQuery] PublishStatus? status,
            [FromQuery] string searchTerm,
            [FromQuery] int? page)
        {
            return await _calcsSearchService.SearchCalculations(specificationId, calculationType, status, searchTerm, page);
        }

        [Route("api/calcs/calculations-search")]
        [HttpPost]
        [Produces(typeof(CalculationSearchResults))]
        public Task<IActionResult> CalculationsSearch([FromBody]SearchModel searchModel)
        {
            return _calcsSearchService.SearchCalculations(searchModel);
        }

        [Obsolete("Migrate to REST method")]
        [Route("api/calcs/calculation-by-id")]
        [HttpGet]
        [Produces(typeof(CalculationResponseModel))]
        public Task<IActionResult> CalculationById([FromQuery] string calculationId)
        {
            return _calcsService.GetCalculationById(calculationId);
        }

        [Route("api/calcs/calculations/by-id/{calculationId}")]
        [HttpGet]
        [Produces(typeof(CalculationResponseModel))]
        public Task<IActionResult> GetCalculationById([FromRoute] string calculationId)
        {
            return _calcsService.GetCalculationById(calculationId);
        }

        [Route("api/calcs/calculation-summaries-for-specification")]
        [HttpGet]
        [Produces(typeof(IEnumerable<CalculationSummaryModel>))]
        public Task<IActionResult> GetCalculationSummariesForSpecification([FromQuery]string specificationId)
        {
            return _calcsService.GetCalculationSummariesForSpecification(specificationId);
        }

        [Route("api/calcs/current-calculations-for-specification")]
        [HttpGet]
        [Produces(typeof(IEnumerable<CalculationResponseModel>))]
        public Task<IActionResult> GetCurrentCalculationsForSpecification([FromQuery]string specificationId)
        {
            return _calcsService.GetCurrentCalculationsForSpecification(specificationId);
        }

        [Route("api/calcs/specifications/{specificationId}/calculations/{calculationId}")]
        [HttpPut]
        [Produces(typeof(CalculationResponseModel))]
        public Task<IActionResult> EditCalculation([FromRoute]string specificationId, [FromRoute]string calculationId, [FromBody]CalculationEditModel model)
        {
            HttpRequest httpRequest = ControllerContext.HttpContext.Request;

            return _calcsService.EditCalculation(specificationId, calculationId, model, httpRequest.GetUser(), httpRequest.GetCorrelationId());
        }

        [Route("api/calcs/specifications/{specificationId}/calculations/{calculationId}/{skipInstruct}")]
        [HttpPut]
        [Produces(typeof(CalculationResponseModel))]
        public Task<IActionResult> EditCalculation([FromRoute]string specificationId, [FromRoute]string calculationId, [FromRoute]bool skipInstruct, [FromBody]CalculationEditModel model)
        {
            HttpRequest httpRequest = ControllerContext.HttpContext.Request;

            return _calcsService.EditCalculation(specificationId, calculationId, model, httpRequest.GetUser(), httpRequest.GetCorrelationId(), skipInstruct: skipInstruct);
        }

        [Route("api/calcs/calculation-version-history")]
        [HttpGet]
        [Produces(typeof(IEnumerable<CalculationVersionResponseModel>))]
        public Task<IActionResult> CalculationVersionHistory([FromQuery]string calculationId)
        {
            return _calcsService.GetCalculationHistory(calculationId);
        }

        [Route("api/calcs/calculation-versions")]
        [HttpPost]
        [Produces(typeof(IEnumerable<CalculationVersionResponseModel>))]
        public Task<IActionResult> CalculationVersions([FromBody]CalculationVersionsCompareModel calculationVersionsCompareModel)
        {
            return _calcsService.GetCalculationVersions(calculationVersionsCompareModel);
        }

        [Route("api/calcs/calculation-edit-status")]
        [HttpPut]
        [Produces(typeof(PublishStatusResultModel))]
        public Task<IActionResult> CalculationEditStatus([FromQuery]string calculationId, [FromBody] EditStatusModel editStatusModel)
        {
            return _calcsService.UpdateCalculationStatus(calculationId, editStatusModel);
        }

        [Route("api/calcs/compile-preview")]
        [HttpPost]
        [Produces(typeof(PreviewResponse))]
        public Task<IActionResult> CompilePreview([FromBody] PreviewRequest previewRequest)
        {
            return _previewService.Compile(previewRequest);
        }

        [Route("api/calcs/get-buildproject-by-specification-id")]
        [HttpGet]
        [Produces(typeof(BuildProject))]
        public Task<IActionResult> GetBuildProjectBySpecificationId([FromQuery]string specificationId)
        {
            return _buildProjectsService.GetBuildProjectBySpecificationId(specificationId);
        }

        [Route("api/calcs/get-calculation-code-context")]
        [HttpGet]
        [Produces(typeof(IEnumerable<TypeInformation>))]
        public Task<IActionResult> GetCalculationCodeContext([FromQuery]string specificationId)
        {
            return _calcsService.GetCalculationCodeContext(specificationId);
        }

        [Route("api/calcs/update-buildproject-relationships")]
        [HttpPost]
        [Produces(typeof(BuildProject))]
        public Task<IActionResult> UpdateBuildProjectRelationships([FromQuery]string specificationId, [FromBody] DatasetRelationshipSummary relationship)
        {
            return _buildProjectsService.UpdateBuildProjectRelationships(specificationId, relationship);
        }

        [Route("api/calcs/reindex")]
        [HttpGet]
        [ProducesResponseType(201)]
        public Task<IActionResult> CalculationReIndex()
        {
            return _calcsService.ReIndex();
        }

        [Route("api/calcs/status-counts")]
        [HttpPost]
        [Produces(typeof(IEnumerable<CalculationStatusCountsModel>))]
        public Task<IActionResult> GetCalculationStatusCounts([FromBody]SpecificationListModel specifications)
        {
            return _calcsService.GetCalculationStatusCounts(specifications);
        }

        [Route("api/calcs/{specificationId}/assembly")]
        [HttpGet]
        [Produces(typeof(byte[]))]
        public Task<IActionResult> GetAssemblyBySpecificationId([FromRoute]string specificationId)
        {
            return _buildProjectsService.GetAssemblyBySpecificationId(specificationId);
        }

        [Route("api/calcs/validate-calc-name/{specificationId}/{calculationName}/{existingCalculationId?}")]
        [HttpGet]
        [ProducesResponseType(200)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> ValidationCalculationName([FromRoute]string specificationId, [FromRoute]string calculationName, [FromRoute]string existingCalculationId)
        {
            return await _calcsService.IsCalculationNameValid(specificationId, calculationName, existingCalculationId);
        }

        [Route("api/calcs/{specificationId}/compileAndSaveAssembly")]
        [HttpGet]
        [ProducesResponseType(201)]
        public async Task<IActionResult> CompileAndSaveAssembly([FromRoute]string specificationId)
        {
            return await _buildProjectsService.CompileAndSaveAssembly(specificationId);
        }

        [Route("api/calcs/{specificationId}/sourceFiles/release")]
        [HttpPost]
        [ProducesResponseType(201)]
        public async Task<IActionResult> SaveSourceFilesRelease([FromRoute]string specificationId)
        {
            return await _buildProjectsService.GenerateAndSaveSourceProject(specificationId, SourceCodeType.Release);
        }

        [Route("api/calcs/{specificationId}/sourceFiles/preview")]
        [HttpPost]
        [ProducesResponseType(201)]
        public async Task<IActionResult> SaveSourceFilesPreview([FromRoute]string specificationId)
        {
            return await _buildProjectsService.GenerateAndSaveSourceProject(specificationId, SourceCodeType.Preview);
        }

        [Route("api/calcs/{specificationId}/sourceFiles/diagnostics")]
        [HttpPost]
        [ProducesResponseType(201)]
        public async Task<IActionResult> SaveSourceFilesDiagnostics([FromRoute]string specificationId)
        {
            return await _buildProjectsService.GenerateAndSaveSourceProject(specificationId, SourceCodeType.Diagnostics);
        }

        [Route("api/calcs/calculation-by-name")]
        [HttpPost]
        [Produces(typeof(CalculationResponseModel))]
        public async Task<IActionResult> GetCalculationByName([FromBody]CalculationGetModel model)
        {
            return await _calcsService.GetCalculationByName(model);
        }

        [Route("api/calcs/specifications/{specificationId}/calculations/metadata")]
        [HttpGet]
        [Produces(typeof(IEnumerable<CalculationMetadata>))]
        public async Task<IActionResult> GetCalculationsMetadata([FromRoute]string specificationId)
        {
            return await _calcsService.GetCalculationsMetadataForSpecification(specificationId);
        }

        [Route("api/calcs/specifications/{specificationId}/calculations")]
        [HttpPost]
        [Produces(typeof(CalculationResponseModel))]
        public async Task<IActionResult> CreateAdditionalCalculation([FromRoute]string specificationId, [FromBody]CalculationCreateModel model)
        {
            HttpRequest httpRequest = ControllerContext.HttpContext.Request;

            return await _calcsService.CreateAdditionalCalculation(specificationId, model, httpRequest.GetUserOrDefault(), httpRequest.GetCorrelationId());
        }

        [Route("api/calcs/specifications/{specificationId}/templates/{fundingStreamId}")]
        [HttpPut]
        [Produces(typeof(TemplateMappingSummary))]
        public async Task<IActionResult> AssociateTemplateIdWithSpecification([FromRoute]string specificationId, [FromRoute]string fundingStreamId, [FromBody] string templateId)
        {
            return await _calcsService.AssociateTemplateIdWithSpecification(specificationId, templateId, fundingStreamId);
        }

        [Route("api/calcs/specifications/{specificationId}/templatemapping/{fundingStreamId}")]
        [HttpGet]
        [Produces(typeof(TemplateMappingSummary))]
        public async Task<IActionResult> GetMappedCalculationsOfSpecificationTemplate([FromRoute]string specificationId, [FromRoute]string fundingStreamId)
        {
            return await _calcsService.GetMappedCalculationsOfSpecificationTemplate(specificationId, fundingStreamId);
        }

        [Route("api/calcs/specifications/{specificationId}/templateCalculations/allApproved")]
        [HttpGet]
        [Produces(typeof(BooleanResponseModel))]
        public async Task<IActionResult> CheckHasAllApprovedTemplateCalculationsForSpecificationId([FromRoute]string specificationId)
        {
            return await _calcsService.CheckHasAllApprovedTemplateCalculationsForSpecificationId(specificationId);
        }

        [HttpPost("api/calcs/specifications/{specificationId}/relationships/reindex")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        public async Task<IActionResult> QueueReIndexSpecificationCalculationsRelationships([FromRoute] string specificationId)
        {
            return await _calculationRelationships.QueueForSpecification(specificationId);
        }
    }
}
