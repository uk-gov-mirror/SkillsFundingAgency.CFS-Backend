﻿using Polly;

namespace CalculateFunding.Services.CalcEngine.Interfaces
{
    public interface ICalculatorResiliencePolicies
    {
        AsyncPolicy CacheProvider { get; set; }

        AsyncPolicy Messenger { get; set; }

        AsyncPolicy ProviderSourceDatasetsRepository { get; set; }

        AsyncPolicy ProviderResultsRepository { get; set; }

        AsyncPolicy CalculationsRepository { get; set; }

        AsyncPolicy JobsApiClient { get; set; }

        AsyncPolicy SpecificationsApiClient { get; set; }

        AsyncPolicy PoliciesApiClient { get; set; }
    }
}
