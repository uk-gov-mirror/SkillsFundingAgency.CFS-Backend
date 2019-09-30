﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Common.ApiClient.Specifications.Models;
using CalculateFunding.Services.Publishing.Interfaces;

namespace CalculateFunding.Publishing.AcceptanceTests.Repositories
{
    public class SpecificationInMemoryRepository : ISpecificationService
    {
        private Dictionary<string, SpecificationSummary> _specifications = new Dictionary<string, SpecificationSummary>();

        public Task<IEnumerable<SpecificationSummary>> GetSpecificationsSelectedForFundingByPeriod(string fundingPeriodId)
        {
            IEnumerable<SpecificationSummary> specifications = _specifications.Values
                 .Where(c => c.IsSelectedForFunding && c.FundingPeriod.Id == fundingPeriodId);

            return Task.FromResult(specifications);
        }

        public Task<SpecificationSummary> GetSpecificationSummaryById(string specificationId)
        {
            SpecificationSummary result = null;
            if (_specifications.TryGetValue(specificationId, out result))
            {
            }

            return Task.FromResult(result);
        }

        public Task SelectSpecificationForFunding(string specificationId)
        {
            throw new NotImplementedException();
        }

        public Task<SpecificationSummary> AddSpecification(SpecificationSummary specificationSummary)
        {
            _specifications.Add(specificationSummary.Id, specificationSummary);
            return Task.FromResult(specificationSummary);
        }
    }
}
