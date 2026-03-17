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
    public class GetGroupFunction
    {
        private readonly ILogger<GetGroupFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public GetGroupFunction(ILogger<GetGroupFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Group> GetGroupAsync([ActivityTrigger] SyncJob syncJob)
        {
            using (_logger.BeginSyncJobScope(syncJob))
            {
                _logger.ActivityFunctionStarted(nameof(GetGroupFunction));
                var group = await _jobTriggerService.GetGroupAsync(syncJob);
                _logger.ActivityFunctionCompleted(nameof(GetGroupFunction));
                return group;
            }
        }
    }
}
