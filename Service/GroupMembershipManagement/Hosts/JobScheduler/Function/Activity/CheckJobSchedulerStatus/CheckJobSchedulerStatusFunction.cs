// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class CheckJobSchedulerStatusFunction
    {
        private readonly HttpClient _httpClient;
        private readonly ILoggingRepository _loggingRepository;

        public CheckJobSchedulerStatusFunction(IHttpClientFactory httpClientFactory, ILoggingRepository loggingRepository)
        {
            _httpClient = httpClientFactory.CreateClient();
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(CheckJobSchedulerStatusFunction))]
        public async Task<bool> CheckStatusAsync([ActivityTrigger] CheckJobSchedulerStatusRequest request)
        {
            var completed = false;
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CheckJobSchedulerStatusFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            
            var response = await _httpClient.GetAsync(new Uri(request.StatusUrl));
            await _loggingRepository.LogMessageAsync(new LogMessage
            { 
                Message = $"Response content for status check is: {await response.Content.ReadAsStringAsync()}" 
            }, VerbosityLevel.INFO);
            
            var responseDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());

            var status = responseDict.GetValueOrDefault("runtimeStatus").ToString();

                
            completed = status == OrchestrationRuntimeStatus.Completed.ToString();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CheckJobSchedulerStatusFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            if (completed)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Status of JobScheduler has been verified as completed at {DateTime.UtcNow}" }, VerbosityLevel.INFO);
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Status of JobScheduler is still pending at {DateTime.UtcNow}" }, VerbosityLevel.INFO);
            }

            return completed;
        }
    }
}
