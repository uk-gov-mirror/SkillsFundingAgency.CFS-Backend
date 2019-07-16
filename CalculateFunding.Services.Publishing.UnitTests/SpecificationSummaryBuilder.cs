﻿using System.Collections.Generic;
using System.Linq;
using CalculateFunding.Common.ApiClient.Policies.Models;
using CalculateFunding.Common.ApiClient.Specifications.Models;
using CalculateFunding.Common.Models;

namespace CalculateFunding.Services.Publishing.UnitTests
{
    public class SpecificationSummaryBuilder : TestEntityBuilder
    {
        private string _id;
        private string _fundingPeriodId;
        private IEnumerable<string> _fundingStreamIds = Enumerable.Empty<string>();

        public SpecificationSummaryBuilder WithId(string id)
        {
            _id = id;

            return this;
        }

        public SpecificationSummaryBuilder WithFundingPeriodId(string fundingPeriodId)
        {
            _fundingPeriodId = fundingPeriodId;

            return this;
        }

        public SpecificationSummaryBuilder WithFundingStreamIds(params string[] fundingStreamIds)
        {
            _fundingStreamIds = fundingStreamIds;

            return this;
        }


        public SpecificationSummary Build()
        {
            return new SpecificationSummary
            {
                Id = _id ?? NewRandomString(),
                FundingPeriod = new Reference(_fundingPeriodId ?? NewRandomString(), NewRandomString()),
                FundingStreams = _fundingStreamIds.Select(_ => new FundingStream
                {
                    Id = _
                }).ToArray()
            };
        }
    }
}