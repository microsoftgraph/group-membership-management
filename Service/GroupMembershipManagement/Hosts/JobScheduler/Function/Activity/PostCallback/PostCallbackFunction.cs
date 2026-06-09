// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class PostCallbackFunction
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PostCallbackFunction> _logger;

        public PostCallbackFunction(IHttpClientFactory httpClientFactory, ILogger<PostCallbackFunction> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(PostCallbackFunction))]
        public async Task PostCallbackAsync([ActivityTrigger] PostCallbackRequest request)
        {
            _logger.FunctionStarted(nameof(PostCallbackFunction));

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", request.AuthToken);
            var requestContent = new StringContent(request.SuccessBody, Encoding.UTF8, "application/json");

            await _httpClient.PostAsync(new Uri(request.CallbackUrl), requestContent);

            _logger.PostCallbackSuccessful(request.CallbackUrl, request.SuccessBody);

            _logger.FunctionCompleted(nameof(PostCallbackFunction));
        }
    }
}
