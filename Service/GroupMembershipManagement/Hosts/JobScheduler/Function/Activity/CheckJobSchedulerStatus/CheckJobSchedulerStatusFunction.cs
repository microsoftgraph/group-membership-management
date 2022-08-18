// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
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
        private readonly ILoggingRepository _loggingRepository;

        public CheckJobSchedulerStatusFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(CheckJobSchedulerStatusFunction))]
        public async Task<bool> CheckStatusAsync([ActivityTrigger] CheckJobSchedulerStatusRequest request)
        {
            var completed = false;
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CheckJobSchedulerStatusFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            
            var client = new HttpClient();
            try
            {
                var response = await client.GetAsync(new Uri(request.StatusUrl));
                await _loggingRepository.LogMessageAsync(new LogMessage
                { 
                    Message = $"Response content for status check is: {await response.Content.ReadAsStringAsync()}" 
                }, VerbosityLevel.DEBUG);
            
                var responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());

                var status = responseDict.GetValueOrDefault("runtimeStatus");

                
                completed = status == OrchestrationRuntimeStatus.Completed.ToString();
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = ex.Message }, VerbosityLevel.INFO);
            }
            finally
            {
                client.Dispose();
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CheckJobSchedulerStatusFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            if (completed)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Status of JobScheduler has been verified as completed at {DateTime.UtcNow}" }, VerbosityLevel.INFO);
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Status is still pending at {DateTime.UtcNow}" }, VerbosityLevel.INFO);
            }

            return completed;
        }
    }
}
