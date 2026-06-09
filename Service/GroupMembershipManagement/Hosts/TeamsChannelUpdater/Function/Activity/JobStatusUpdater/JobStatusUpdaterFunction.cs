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
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public JobStatusUpdaterFunction(
                        ILogger<JobStatusUpdaterFunction> logger,
                        ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob);

            _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));

            var syncJob = await _teamsChannelUpdaterService.GetSyncJobAsync(request.JobId);

            if (syncJob != null)
            {
                syncJob.ThresholdViolations = request.ThresholdViolations;

                if (request.Status == SyncStatus.Idle && syncJob.IgnoreThresholdOnce)
                    syncJob.IgnoreThresholdOnce = false;

                await _teamsChannelUpdaterService.UpdateSyncJobStatusAsync(syncJob, request.Status, false, request.SyncJob.RunId.GetValueOrDefault());
            }

            _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
        }
    }
}
