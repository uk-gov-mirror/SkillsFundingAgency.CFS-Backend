﻿using CalculateFunding.Services.CalcEngine.Interfaces;
using CalculateFunding.Services.DeadletterProcessor;
using Polly;

namespace CalculateFunding.Services.CalcEngine
{
    public class CalculatorResiliencePolicies : ICalculatorResiliencePolicies, IJobHelperResiliencePolicies
    {
        public AsyncPolicy CacheProvider { get; set; }

        public AsyncPolicy Messenger { get; set; }

        public AsyncPolicy ProviderSourceDatasetsRepository { get; set; }

        public AsyncPolicy ProviderResultsRepository { get; set; }

        public AsyncPolicy CalculationsRepository { get; set; }

        public AsyncPolicy JobsApiClient { get; set; }

        public AsyncPolicy SpecificationsApiClient { get; set; }

        public AsyncPolicy PoliciesApiClient { get; set; }
    }
}
