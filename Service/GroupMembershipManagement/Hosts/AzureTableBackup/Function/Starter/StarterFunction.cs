// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;

namespace Hosts.AzureTableBackup
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        
        [FunctionName(nameof(StarterFunction))]
        public async Task Run(
            [TimerTrigger("%backupTriggerSchedule%")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" });
            
            await starter.StartNewAsync(nameof(OrchestratorFunction), null);
           
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" });
        }
    }
}
