﻿using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using CalculateFunding.Api.External.V2.Interfaces;
using CalculateFunding.Services.Specs.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CalculateFunding.Api.External.V2.Services
{
    public class TimePeriodsService : ITimePeriodsService
    {
        private readonly ISpecificationsService _specificationsService;
        private readonly IMapper _mapper;

        public TimePeriodsService(ISpecificationsService specificationsService, IMapper mapper)
        {
            _specificationsService = specificationsService;
            _mapper = mapper;
        }


        public async Task<IActionResult> GetFundingPeriods(HttpRequest request)
        {
            IActionResult actionResult = await _specificationsService.GetFundingPeriods(request);

            if (actionResult is OkObjectResult okObjectResult)
            {
                IEnumerable<CalculateFunding.Models.Specs.Period> periods = (IEnumerable<CalculateFunding.Models.Specs.Period>)okObjectResult.Value;
                List<Models.Period> mappedPeriods = _mapper.Map<List<Models.Period>>(periods);
                return new OkObjectResult(mappedPeriods);
            }
            return actionResult;
        }
    }
}
