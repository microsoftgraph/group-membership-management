// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class GetChannelFunction
    {
        private readonly ILogger<GetChannelFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public GetChannelFunction(ILogger<GetChannelFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(GetChannelFunction))]
        public async Task<Channel> GetChannelAsync([ActivityTrigger] SyncJob syncJob)
        {
            using (_logger.BeginSyncJobScope(syncJob))
            {
                _logger.FunctionStarted(nameof(GetChannelFunction));
                var channel = await _jobTriggerService.GetChannelAsync(syncJob);
                _logger.FunctionCompleted(nameof(GetChannelFunction));
                return channel;
            }
        }
    }
}
