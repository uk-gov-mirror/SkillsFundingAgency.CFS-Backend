﻿using System.Threading.Tasks;
using CalculateFunding.Functions.Common;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Specs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;

namespace CalculateFunding.Functions.Calcs.Http
{
    public static class Calculations
    {
        [FunctionName("calculations")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", "get")] HttpRequest req, TraceWriter log)
        {
            var restMethods = new RestGetMethods<CalculationImplementation>();
            return await restMethods.Run(req, log, "specificationId");
        }
    }
}
