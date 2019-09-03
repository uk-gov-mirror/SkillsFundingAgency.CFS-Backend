﻿using CalculateFunding.Api.External.V3.Interfaces;
using Polly;

namespace CalculateFunding.Api.External.V3.Services
{
    public class ExternalApiResiliencePolicies : IExternalApiResiliencePolicies
    {
        public Policy BlobRepositoryPolicy
        {
            get; set;
        }
    }
}
