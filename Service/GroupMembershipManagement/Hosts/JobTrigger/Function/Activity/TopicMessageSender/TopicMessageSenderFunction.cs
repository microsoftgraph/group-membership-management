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
    public class TopicMessageSenderFunction
    {
        private readonly ILogger<TopicMessageSenderFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public TopicMessageSenderFunction(ILogger<TopicMessageSenderFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] SyncJob syncJob)
        {
            if (syncJob != null)
            {
                using (_logger.BeginSyncJobScope(syncJob))
                {
                    _logger.FunctionStarted(nameof(TopicMessageSenderFunction));
                    await _jobTriggerService.SendMessageAsync(syncJob);
                    _logger.FunctionCompleted(nameof(TopicMessageSenderFunction));
                }
            }
        }
    }
}
