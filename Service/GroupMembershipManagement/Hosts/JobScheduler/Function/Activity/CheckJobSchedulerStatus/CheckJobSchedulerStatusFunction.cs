// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class CheckJobSchedulerStatusFunction
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CheckJobSchedulerStatusFunction> _logger;

        public CheckJobSchedulerStatusFunction(IHttpClientFactory httpClientFactory, ILogger<CheckJobSchedulerStatusFunction> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(CheckJobSchedulerStatusFunction))]
        public async Task<bool> CheckStatusAsync([ActivityTrigger] CheckJobSchedulerStatusRequest request)
        {
            var completed = false;
            _logger.FunctionStarted(nameof(CheckJobSchedulerStatusFunction));

            var response = await _httpClient.GetAsync(new Uri(request.StatusUrl));
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.StatusCheckResponse(responseContent);

            var responseDict = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);

            var status = responseDict.GetValueOrDefault("runtimeStatus").ToString();

            completed = status == "Completed";

            _logger.FunctionCompleted(nameof(CheckJobSchedulerStatusFunction));

            if (completed)
            {
                _logger.JobSchedulerCompleted(DateTime.UtcNow);
            }
            else
            {
                _logger.JobSchedulerPending(DateTime.UtcNow);
            }

            return completed;
        }
    }
}
