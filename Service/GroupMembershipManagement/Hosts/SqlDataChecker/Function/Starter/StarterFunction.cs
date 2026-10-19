// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Hosts.SqlDataChecker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace SqlDataChecker
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger;
        }

        [Function(nameof(StarterFunction))]
        public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req, [DurableClient] DurableTaskClient starter)
        {
            _logger.FunctionStarted(nameof(StarterFunction));

            StarterRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<StarterRequest>(
                    req.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return await CreateBadRequestAsync(req);
            }

            if (request is null
                || !Guid.TryParse(request.RunId, out var runId)
                || runId == Guid.Empty)
            {
                return await CreateBadRequestAsync(req);
            }

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(
                nameof(OrchestratorFunction),
                runId);
            var response = await starter.CreateCheckStatusResponseAsync(req, instanceId);

            _logger.FunctionCompleted(nameof(StarterFunction));

            return response;
        }

        private sealed class StarterRequest
        {
            public string? RunId { get; set; }
        }

        private static async Task<HttpResponseData> CreateBadRequestAsync(HttpRequestData req)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A non-empty runId GUID is required.");
            return badRequest;
        }
    }
}
