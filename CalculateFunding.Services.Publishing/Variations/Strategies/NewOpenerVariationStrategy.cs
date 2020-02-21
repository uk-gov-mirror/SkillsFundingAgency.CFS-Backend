﻿using System.Collections.Generic;
using System.Threading.Tasks;
using CalculateFunding.Services.Publishing.Interfaces;
using CalculateFunding.Services.Publishing.Models;

namespace CalculateFunding.Services.Publishing.Variations.Strategies
{
    public class NewOpenerVariationStrategy : IVariationStrategy
    {
        public string Name => "NewOpener";

        public Task DetermineVariations(ProviderVariationContext providerVariationContext, IEnumerable<string> fundingLineCodes)
        {
            return Task.CompletedTask;
        }
    }
}
