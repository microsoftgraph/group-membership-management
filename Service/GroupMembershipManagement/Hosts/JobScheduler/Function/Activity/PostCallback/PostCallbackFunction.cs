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
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class PostCallbackFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public PostCallbackFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(PostCallbackFunction))]
        public async Task PostCallbackAsync([ActivityTrigger] PostCallbackRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PostCallbackFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            var client = new HttpClient();
            try
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", request.AuthToken);
                var requestContent = new StringContent(request.SuccessBody, Encoding.UTF8, "application/json");

                await client.PostAsync(new Uri(request.CallbackUrl), requestContent);

                await _loggingRepository.LogMessageAsync(new LogMessage
                    { 
                        Message = $"Successfully posted to url '{request.CallbackUrl}' with following body: {request.SuccessBody}"  
                    }, VerbosityLevel.INFO);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = ex.Message }, VerbosityLevel.INFO);
            }
            finally
            {
                client.Dispose();
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PostCallbackFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
