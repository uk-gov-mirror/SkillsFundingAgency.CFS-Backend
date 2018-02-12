﻿using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Core.Interfaces.Logging;
using CalculateFunding.Services.Core.Interfaces.Proxies;
using CalculateFunding.Services.Core.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Core.Proxies
{
    public class ApiClientProxy : IApiClientProxy
    {
        private const string SfaCorellationId = "sfa-correlationId";
        private const string SfaUsernameProperty = "sfa-username";
        private const string SfaUserIdProperty = "sfa-userid";

        private const string OcpApimSubscriptionKey = "Ocp-Apim-Subscription-Key";

        private readonly ILogger _logger;

        private readonly IHttpClient _httpClient;
        private readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings { Formatting = Formatting.Indented, ContractResolver = new CamelCasePropertyNamesContractResolver() };
        private readonly ICorrelationIdProvider _correlationIdProvider;

        public ApiClientProxy(ApiOptions options, IHttpClient httpClient, ILogger logger, ICorrelationIdProvider correlationIdProvider)
        {
            Guard.ArgumentNotNull(options, nameof(options));
            Guard.ArgumentNotNull(httpClient, nameof(httpClient));
            Guard.ArgumentNotNull(httpClient, nameof(logger));
            Guard.ArgumentNotNull(httpClient, nameof(correlationIdProvider));

            _correlationIdProvider = correlationIdProvider;

            _httpClient = httpClient;
            string baseAddress = options.ApiEndpoint;
            if (!baseAddress.EndsWith("/", StringComparison.CurrentCulture))
            {
                baseAddress = $"{baseAddress}/";
            }

            _httpClient.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
            _httpClient.DefaultRequestHeaders?.Add(OcpApimSubscriptionKey, options.ApiKey);
            _httpClient.DefaultRequestHeaders?.Add(SfaCorellationId, _correlationIdProvider.GetCorrelationId());
            _httpClient.DefaultRequestHeaders?.Add(SfaUsernameProperty, "testuser");
            _httpClient.DefaultRequestHeaders?.Add(SfaUserIdProperty, "b001af14-3754-4cb1-9980-359e850700a8");

            _httpClient.DefaultRequestHeaders?.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders?.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            _httpClient.DefaultRequestHeaders?.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            _logger = logger;
        }

        public async Task<T> GetAsync<T>(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException(nameof(url));
            }

            HttpResponseMessage response = await _httpClient.GetAsync(url);

            if (response == null)
            {
                throw new HttpRequestException($"Unable to connect to server. Url={_httpClient.BaseAddress.AbsoluteUri}{url}");
            }

            if (response.IsSuccessStatusCode)
            {
                string bodyContent = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(bodyContent, _serializerSettings);
            }

            return default(T);
        }
    }
}
