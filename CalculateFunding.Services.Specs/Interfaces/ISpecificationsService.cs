﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Specs.Interfaces
{
    public interface ISpecificationsService
    {
        Task<IActionResult> CreateSpecification(HttpRequest request);

        Task<IActionResult> GetSpecifications(HttpRequest req);

        Task<IActionResult> GetSpecificationById(HttpRequest request);

        Task<IActionResult> GetSpecificationByAcademicYearId(HttpRequest request);

        Task<IActionResult> GetSpecificationByName(HttpRequest request);

        Task<IActionResult> GetAcademicYears(HttpRequest request);

        Task<IActionResult> GetFundingStreams(HttpRequest request);

        Task<IActionResult> GetAllocationLines(HttpRequest request);

        Task<IActionResult> GetPolicyByName(HttpRequest request);

        Task<IActionResult> CreatePolicy(HttpRequest request);

        Task<IActionResult> CreateCalculation(HttpRequest request);

        Task<IActionResult> GetCalculationByName(HttpRequest request);

        Task<IActionResult> GetCalculationBySpecificationIdAndCalculationId(HttpRequest request);

        Task<IActionResult> GetCalculationsBySpecificationId(HttpRequest request);

        Task AssignDataDefinitionRelationship(Message message);

        Task<IActionResult> ReIndex();

        Task<IActionResult> SaveFundingStream(HttpRequest request);
    }
}
