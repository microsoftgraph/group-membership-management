// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName("ServiceBusStarterFunction")]
        public async Task ProcessServiceBusMessageAsync(
            [ServiceBusTrigger("%serviceBusMembershipAggregatorQueue%", Connection = "serviceBusConnectionString")] ServiceBusReceivedMessage message,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            var request = JsonConvert.DeserializeObject<MembershipAggregatorHttpRequest>(Encoding.UTF8.GetString(message.Body));
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Processing message {message.MessageId}", RunId = runId }, VerbosityLevel.INFO);

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId}", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }

            [FunctionName(nameof(StarterFunction))]
        public async Task<HttpResponseMessage> RunAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            string content = null;
            if (req.Content != null)
            {
                content = await req.Content.ReadAsStringAsync();
            }

            var (IsValid, Response) = await IsValidRequestAsync(content);
            if (!IsValid)
            {
                return Response;
            }

            var request = JsonConvert.DeserializeObject<MembershipAggregatorHttpRequest>(content);
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = content, RunId = runId });

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"MembershipAggregator instance id: {instanceId}", RunId = runId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);

            return Response;
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

            var request = JsonConvert.DeserializeObject<MembershipAggregatorHttpRequest>(content);

            if (string.IsNullOrWhiteSpace(request.FilePath) || request.PartsCount <= 0)
            {
                var msg = "Request is not valid.";
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"{nameof(StarterFunction)}. {msg}",
                    DynamicProperties = request.SyncJob?.ToDictionary()
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
