// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Repositories.Contracts;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly SGMembershipCalculator _calculator;

        public StarterFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _loggingRepository = loggingRepository;
            _calculator = calculator;
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task Run([ServiceBusTrigger("%serviceBusSyncJobTopic%", "SecurityGroup", Connection = "serviceBusTopicConnection")] Message message,
                              [DurableClient] IDurableOrchestrationClient starter,
                              ILogger log)
        {
            var syncJob = JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body));
            _loggingRepository.SyncJobProperties = syncJob.ToDictionary();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = syncJob.RunId });
            await starter.StartNewAsync(nameof(OrchestratorFunction), syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = syncJob.RunId });
        }
    }
}