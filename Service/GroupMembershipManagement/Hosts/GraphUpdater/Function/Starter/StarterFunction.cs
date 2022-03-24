// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IConfiguration _configuration = null;

        public StarterFunction(ILoggingRepository loggingRepository, IConfiguration configuration)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task<HttpResponseMessage> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
        [DurableClient] IDurableOrchestrationClient starter)
        {
            string content = null;
            if(req.Content != null)
            {
                content = await req.Content.ReadAsStringAsync();
            }

            var validationInfo = await IsValidRequestAsync(content);
            if (!validationInfo.IsValid)
            {
                return validationInfo.Response;
            }

            var request = JsonConvert.DeserializeObject<GraphUpdaterHttpRequest>(content);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(StarterFunction)} function started",
                RunId = request.RunId
            });

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = content,
                RunId = request.RunId,
            });

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"InstanceId: {instanceId}",
                RunId = request.RunId
            });

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(StarterFunction)} function completed",
                RunId = request.RunId
            });

            return validationInfo.Response;
        }

        private async Task<(bool IsValid, HttpResponseMessage Response)> IsValidRequestAsync(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                var msg = "Request content is empty.";
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"{nameof(StarterFunction)}. {msg}",
                });

                return
                (
                    false,
                    new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.BadRequest,
                        Content = new StringContent(msg)
                    }
                );
            }

            var request = JsonConvert.DeserializeObject<GraphUpdaterHttpRequest>(content);

            if (string.IsNullOrWhiteSpace(request.FilePath))
            {
                var msg = "Request is not valid, FilePath is missing.";
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"{nameof(StarterFunction)}. {msg}",
                    RunId = request.RunId,
                });

                return
                (
                    false,
                    new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.BadRequest,
                        Content = new StringContent(msg)
                    }
                );
            }

            return (true, new HttpResponseMessage { StatusCode = HttpStatusCode.NoContent });
        }
    }
}