﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Common.ApiClient.Jobs;
using CalculateFunding.Common.ApiClient.Jobs.Models;
using CalculateFunding.Common.ApiClient.Models;
using CalculateFunding.Common.Utility;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Publishing.Interfaces;
using Polly;

namespace CalculateFunding.Services.Publishing
{
    public class CalculationEngineRunningChecker : ICalculationEngineRunningChecker
    {
        private readonly IJobsApiClient _jobsApiClient;
        private readonly Policy _resiliencePolicy;


        public CalculationEngineRunningChecker(
            IJobsApiClient jobsApiClient,
            IPublishingResiliencePolicies publishingResiliencePolicies)
        {
            Guard.ArgumentNotNull(jobsApiClient, nameof(jobsApiClient));
            Guard.ArgumentNotNull(publishingResiliencePolicies?.JobsApiClient, nameof(publishingResiliencePolicies.JobsApiClient));

            _jobsApiClient = jobsApiClient;
            _resiliencePolicy = publishingResiliencePolicies.JobsApiClient;
        }

        public async Task<bool> IsCalculationEngineRunning(string specificationId, IEnumerable<string> jobTypes)
        {
            Guard.ArgumentNotNull(jobTypes, nameof(jobTypes));

            IEnumerable<Task<ApiResponse<JobSummary>>> jobResponses = jobTypes
                .Select(async _ => await _resiliencePolicy.ExecuteAsync(() => _jobsApiClient.GetLatestJobForSpecification(specificationId, new string[] { _ })));

            await TaskHelper.WhenAllAndThrow(jobResponses.ToArraySafe());

            return jobResponses.Any(_ =>  _.Result?.Content != null && ((JobSummary)_.Result?.Content).RunningStatus == RunningStatus.InProgress);
        }
    }
}
