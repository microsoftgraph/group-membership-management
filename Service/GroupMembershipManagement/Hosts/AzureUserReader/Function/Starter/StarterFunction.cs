// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using AzureUserReader.Orchestrator;
using AzureUserReader.Requests;
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AzureUserReader.Starter
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" });

            HttpResponseMessage response;
            var result = await ValidateRequestAsync(req);

            if (result.StatusCode == HttpStatusCode.OK)
            {
                var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), result.Request);
                response = starter.CreateCheckStatusResponse(req, instanceId);
            }
            else
            {
                response = new HttpResponseMessage { StatusCode = result.StatusCode };
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" });

            return response;
        }

        private async Task<(HttpStatusCode StatusCode, AzureUserReaderRequest Request)> ValidateRequestAsync(HttpRequestMessage request)
        {
            AzureUserReaderRequest userReaderRequest = null;

            try
            {
                var content = await request.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(content))
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Request body was not provided." });
                    return (HttpStatusCode.BadRequest, null);
                }

                userReaderRequest = JsonConvert.DeserializeObject<AzureUserReaderRequest>(content);

                if (string.IsNullOrWhiteSpace(userReaderRequest.ContainerName) || string.IsNullOrWhiteSpace(userReaderRequest.BlobPath))
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Request body is not valid." });
                    return (HttpStatusCode.BadRequest, null);
                }
            }
            catch (Exception ex) when (ex.GetType() == typeof(JsonReaderException) || ex.GetType() == typeof(JsonSerializationException))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Request body is not valid." });
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