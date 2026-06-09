// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class JobReaderFunction
    {
        private readonly ILogger<JobReaderFunction> _logger;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public JobReaderFunction(ILogger<JobReaderFunction> logger, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(JobReaderFunction))]
        public async Task<SyncJob> GetSyncJobAsync([ActivityTrigger] JobReaderRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob);

            _logger.FunctionStarted(nameof(JobReaderFunction));
            var syncJob = await _teamsChannelUpdaterService.GetSyncJobAsync(request.JobId);
            _logger.FunctionCompleted(nameof(JobReaderFunction));
            return syncJob;
        }
    }
}

