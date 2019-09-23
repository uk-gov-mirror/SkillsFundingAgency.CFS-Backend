﻿using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Tests.Common.Helpers;
using System.Collections.Generic;

namespace CalculateFunding.Services.Calcs.UnitTests
{
    public class JobSummaryBuilder : TestEntityBuilder
    {
        private RunningStatus _inProgressStatus;
        private string _jobType;

        public JobSummaryBuilder WithRunningStatus(RunningStatus inProgressStatus)
        {
            _inProgressStatus = inProgressStatus;

            return this;
        }

        public JobSummaryBuilder WithJobType(string jobType)
        {
            _jobType = jobType;

            return this;
        }

        public JobSummary Build()
        {
            return new JobSummary
            {
                RunningStatus = _inProgressStatus,
                JobType = _jobType
            };
        }
    }
}