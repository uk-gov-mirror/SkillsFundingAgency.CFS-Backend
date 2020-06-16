﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models;
using CalculateFunding.Models.Policy;
using CalculateFunding.Models.Policy.TemplateBuilder;
using CalculateFunding.Repositories.Common.Search.Results;
using CalculateFunding.Services.Policy.Interfaces;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Policy.Models;
using CalculateFunding.Services.Policy.TemplateBuilder;
using CalculateFunding.Services.Policy.Validators;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CalculateFunding.Api.Policy.Controllers
{
    [ApiController]
    public class TemplateBuildController : ControllerBase
    {
        private readonly ITemplateBuilderService _templateBuilderService;
        private readonly IIoCValidatorFactory _validatorFactory;
        private readonly TemplateSearchService _templateSearchService;

        public TemplateBuildController(
            ITemplateBuilderService templateBuilderService,
            IIoCValidatorFactory validatorFactory,
            TemplateSearchService templateSearchService)
        {
            _templateBuilderService = templateBuilderService;
            _validatorFactory = validatorFactory;
            _templateSearchService = templateSearchService;
        }

        [HttpGet("api/templates/build/{templateId}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TemplateResponse))]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTemplate([FromRoute] string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return new BadRequestObjectResult("Null or empty template id");
            }
            
            TemplateResponse result = await _templateBuilderService.GetTemplate(templateId);

            if (result == null)
                return NotFound();

            return Ok(result);
        }

        [HttpGet("api/templates/build/{templateId}/versions/{version}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TemplateResponse))]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTemplateVersion([FromRoute] string templateId, [FromRoute] string version)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return new BadRequestObjectResult("Null or empty template id");
            }
            if (string.IsNullOrWhiteSpace(version))
            {
                return new BadRequestObjectResult("Null or empty template version");
            }
            
            TemplateResponse result = await _templateBuilderService.GetTemplateVersion(templateId, version);

            if (result == null)
                return NotFound();

            return Ok(result);
        }

        [HttpPost("api/templates/build")]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(string))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateTemplate(TemplateCreateCommand command)
        {
            ValidationResult validationResult = await _validatorFactory.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            Reference author = ControllerContext.HttpContext.Request?.GetUserOrDefault();

            CommandResult result = await _templateBuilderService.CreateTemplate(command, author);

            if (result.Succeeded)
            {
                return new CreatedResult($"api/templates/build/{result.TemplateId}", result.TemplateId);
            }

            if (result.ValidationResult != null)
            {
                return result.ValidationResult.AsBadRequest();
            }

            return new InternalServerErrorResult(result.ErrorMessage ?? result.Exception?.Message ?? "Unknown error occurred");
        }

        [HttpPost("api/templates/build/clone")]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(string))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateTemplateAsClone(TemplateCreateAsCloneCommand command)
        {
            ValidationResult validationResult = await _validatorFactory.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            Reference author = ControllerContext.HttpContext.Request?.GetUserOrDefault();

            CommandResult result = await _templateBuilderService.CreateTemplateAsClone(command, author);

            if (result.Succeeded)
            {
                return new CreatedResult($"api/templates/build/{result.TemplateId}", result.TemplateId);
            }

            if (result.ValidationResult != null)
            {
                return result.ValidationResult.AsBadRequest();
            }

            return new InternalServerErrorResult(result.ErrorMessage ?? result.Exception?.Message ?? "Unknown error occurred");
        }

        [HttpPut("api/templates/build/content")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTemplateContent(TemplateFundingLinesUpdateCommand command)
        {
            ValidationResult validationResult = await _validatorFactory.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            Reference author = ControllerContext.HttpContext.Request?.GetUserOrDefault();

            CommandResult result = await _templateBuilderService.UpdateTemplateContent(command, author);

            if (result.Succeeded)
            {
                return Ok();
            }
            if (result.ValidationModelState != null)
            {
                return BadRequest(result.ValidationModelState);
            }

            return new InternalServerErrorResult(result.ErrorMessage ?? result.Exception?.Message ?? "Unknown error occurred");
        }

        [HttpPut("api/templates/build/metadata")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTemplateMetadata(TemplateMetadataUpdateCommand command)
        {
            ValidationResult validationResult = await _validatorFactory.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            Reference author = ControllerContext.HttpContext.Request?.GetUserOrDefault();

            CommandResult result = await _templateBuilderService.UpdateTemplateMetadata(command, author);

            if (result.Succeeded)
            {
                return Ok();
            }

            if (result.ValidationResult != null)
            {
                return result.ValidationResult.AsBadRequest();
            }

            return new InternalServerErrorResult(result.ErrorMessage ?? result.Exception?.Message ?? "Unknown error occurred");
        }

        [HttpGet("api/templates/build/{templateId}/versions")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<TemplateResponse>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTemplateVersions([FromRoute] string templateId, [FromQuery] string statuses)
        {
            List<TemplateStatus> templateStatuses = !string.IsNullOrWhiteSpace(statuses) ? statuses.Split(',')
                .Select(s => (TemplateStatus)Enum.Parse(typeof(TemplateStatus), s))
                .ToList() : new List<TemplateStatus>();

            IEnumerable<TemplateResponse> templateVersionResponses =
                await _templateBuilderService.GetVersionsByTemplate(templateId, templateStatuses);

            return Ok(templateVersionResponses);
        }

        [HttpGet("api/templates/build/available-stream-periods")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<FundingStreamWithPeriods>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetFundingStreamPeriodsWithoutTemplates()
        {
            IEnumerable<FundingStreamWithPeriods> results = await _templateBuilderService.GetFundingStreamAndPeriodsWithoutTemplates();
            return Ok(results);
        }

        [HttpPost("api/templates/build/versions/search")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<TemplateResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTemplateVersions(FindTemplateVersionQuery query)
        {
            ValidationResult validationResult = await _validatorFactory.Validate(query);
            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            if(!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            IEnumerable<TemplateResponse> templateVersionResponses =
                await _templateBuilderService.FindVersionsByFundingStreamAndPeriod(query);

            return Ok(templateVersionResponses);
        }

        [HttpPost("api/templates/build/{templateId}/publish")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CommandResult))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PublishTemplate([FromRoute] string templateId, [FromBody] TemplatePublishCommand command)
        {
            Guard.ArgumentNotNull(command, nameof(command));
            
            command.Author = ControllerContext.HttpContext.Request?.GetUserOrDefault();
            
            CommandResult response = await _templateBuilderService.PublishTemplate(command);

            if (response.Succeeded)
            {
                return Ok(response);
            }

            if (response.ValidationResult != null)
            {
                return response.ValidationResult.AsBadRequest();
            }

            return new InternalServerErrorResult(response.ErrorMessage ?? response.Exception?.Message ?? "Unknown error occurred");
        }

        [Route("api/templates/templates-search")]
        [HttpPost]
        [Produces(typeof(TemplateSearchResults))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> TemplatesSearch([FromBody] SearchModel searchModel)
        {
            return await _templateSearchService.SearchTemplates(searchModel);
        }

        [HttpGet("api/templates/reindex")]
        [ProducesResponseType(204)]
        public async Task<IActionResult> ReIndex()
        {
          return await _templateSearchService.ReIndex(GetUser(),
            GetCorrelationId());
        }

        private Reference GetUser()
        {
          return Request.GetUser();
        }

        private string GetCorrelationId()
        {
          return Request.GetCorrelationId();
        }
  }
}