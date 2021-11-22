// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Entities;

namespace Hosts.AzureTableBackup
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        /* public async Task Run(
             [TimerTrigger("%backupTriggerSchedule%")] TimerInfo myTimer,
             [DurableClient] IDurableOrchestrationClient starter,
             ILogger log)*/
        [FunctionName(nameof(StarterFunction))]
        public async Task<HttpResponseMessage> HttpStart([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
             ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" });
            
            await starter.StartNewAsync(nameof(OrchestratorFunction), null);
           
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" });
        
            return new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
        }
    }
}
