﻿using CalculateFunding.Common.Models;
using CalculateFunding.Models.Publishing;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Publishing.Interfaces
{
    public interface IPublishedProviderStatusUpdateService
    {
        Task UpdatePublishedProviderStatus(IEnumerable<PublishedProvider> publishedProviders, Reference author, PublishedProviderStatus publishedProviderStatus);
    }
}
