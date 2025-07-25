// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Models;
using Repositories.Contracts;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(StarterFunction))]
        public async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);

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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);

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
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Request body was not provided." });
                    return (HttpStatusCode.BadRequest, null);
                }

                userReaderRequest = JsonSerializer.Deserialize<AzureUserReaderRequest>(content);

                if (string.IsNullOrWhiteSpace(userReaderRequest.ContainerName) || string.IsNullOrWhiteSpace(userReaderRequest.BlobPath))
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Request body is not valid." });
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
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Request body is not valid. TenantInformation is missing." });
                        return (HttpStatusCode.BadRequest, null);
                    }
                }
            }
            catch (JsonException)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = "Request body is not valid." });
                return (HttpStatusCode.BadRequest, null);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unexpected error occured when processing the request.\n{ex}" });
                return (HttpStatusCode.InternalServerError, null);
            }

            return (HttpStatusCode.OK, userReaderRequest);
        }
    }
}