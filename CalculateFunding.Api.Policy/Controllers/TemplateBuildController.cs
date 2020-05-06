﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Common.Models;
using CalculateFunding.Models.Policy.TemplateBuilder;
using CalculateFunding.Services.Policy.Interfaces;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Policy.Models;
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

        public TemplateBuildController(
            ITemplateBuilderService templateBuilderService,
            IIoCValidatorFactory validatorFactory)
        {
            _templateBuilderService = templateBuilderService;
            _validatorFactory = validatorFactory;
        }

        [HttpGet("api/templates/build/{templateId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
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
        [ProducesResponseType(StatusCodes.Status200OK)]
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
            ValidationResult validationResult = _validatorFactory.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            Reference author = ControllerContext.HttpContext.Request?.GetUserOrDefault();

            CreateTemplateResponse result = await _templateBuilderService.CreateTemplate(command, author);

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
        public async Task<IActionResult> UpdateTemplateContent(TemplateContentUpdateCommand command)
        {
            ValidationResult validationResult = _validatorFactory.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            Reference author = ControllerContext.HttpContext.Request?.GetUserOrDefault();

            UpdateTemplateContentResponse result = await _templateBuilderService.UpdateTemplateContent(command, author);

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
            ValidationResult validationResult = _validatorFactory.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult.AsBadRequest();
            }

            Reference author = ControllerContext.HttpContext.Request?.GetUserOrDefault();

            UpdateTemplateMetadataResponse result = await _templateBuilderService.UpdateTemplateMetadata(command, author);

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
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTemplateVersions([FromRoute] string templateId, [FromQuery] string statuses)
        {
            List<TemplateStatus> templateStatuses = !string.IsNullOrWhiteSpace(statuses) ? statuses.Split(',')
                .Select(s => (TemplateStatus)Enum.Parse(typeof(TemplateStatus), s))
                .ToList() : new List<TemplateStatus>();

            IEnumerable<TemplateVersionResponse> templateVersionResponses =
                await _templateBuilderService.GetTemplateVersions(templateId, templateStatuses);

            return Ok(templateVersionResponses);
        }
    }
}