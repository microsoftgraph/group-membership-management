// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class PostCallbackFunction
    {
        private readonly HttpClient _httpClient;
        private readonly ILoggingRepository _loggingRepository;

        public PostCallbackFunction(IHttpClientFactory httpClientFactory, ILoggingRepository loggingRepository)
        {
            _httpClient = httpClientFactory.CreateClient();
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(PostCallbackFunction))]
        public async Task PostCallbackAsync([ActivityTrigger] PostCallbackRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PostCallbackFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", request.AuthToken);
            var requestContent = new StringContent(request.SuccessBody, Encoding.UTF8, "application/json");

            await _httpClient.PostAsync(new Uri(request.CallbackUrl), requestContent);

            await _loggingRepository.LogMessageAsync(new LogMessage
                { 
                    Message = $"Successfully posted to url '{request.CallbackUrl}' with following body: {request.SuccessBody}"  
                }, VerbosityLevel.INFO);
           

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PostCallbackFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
