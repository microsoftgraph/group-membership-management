// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(StarterFunction))]
        public async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient starter)
        {
            _logger.FunctionStarted(nameof(StarterFunction));

            HttpResponseData response;
            var result = await ValidateRequestAsync(req);

            if (result.StatusCode == HttpStatusCode.OK)
            {
                var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), result.Request);
                response = await starter.CreateCheckStatusResponseAsync(req, instanceId);
            }
            else
            {
                response = req.CreateResponse(result.StatusCode);
            }

            _logger.FunctionCompleted(nameof(StarterFunction));

            return response;
        }

        private async Task<(HttpStatusCode StatusCode, AzureUserReaderRequest Request)> ValidateRequestAsync(HttpRequestData request)
        {
            AzureUserReaderRequest userReaderRequest = null;

            try
            {
                var content = await request.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.RequestBodyNotProvided();
                    return (HttpStatusCode.BadRequest, null);
                }

                userReaderRequest = JsonSerializer.Deserialize<AzureUserReaderRequest>(content);

                if (string.IsNullOrWhiteSpace(userReaderRequest.ContainerName) || string.IsNullOrWhiteSpace(userReaderRequest.BlobPath))
                {
                    _logger.RequestBodyNotValid();
                    return (HttpStatusCode.BadRequest, null);
                }

                if (userReaderRequest.ShouldCreateNewUsers)
                {
                    if (userReaderRequest.TenantInformation == null ||
                        string.IsNullOrWhiteSpace(userReaderRequest.TenantInformation.TenantDomain) ||
                        string.IsNullOrWhiteSpace(userReaderRequest.TenantInformation.EmailPrefix) ||
                        string.IsNullOrWhiteSpace(userReaderRequest.TenantInformation.CountryCode)
                        )
                    {
                        _logger.TenantInformationMissing();
                        return (HttpStatusCode.BadRequest, null);
                    }
                }
            }
            catch (JsonException)
            {
                _logger.RequestBodyNotValid();
                return (HttpStatusCode.BadRequest, null);
            }
            catch (Exception ex)
            {
                _logger.UnexpectedRequestError(ex);
                return (HttpStatusCode.InternalServerError, null);
            }

            return (HttpStatusCode.OK, userReaderRequest);
        }
    }
}