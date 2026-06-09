// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public JobStatusUpdaterFunction(
                        ILogger<JobStatusUpdaterFunction> logger,
                        IGraphUpdaterService graphUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));

            var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.SyncJob.Id);
            syncJob.ThresholdViolations = request.ThresholdViolations;
            if (request.Status == SyncStatus.Idle && syncJob.IgnoreThresholdOnce) syncJob.IgnoreThresholdOnce = false;

            if (syncJob != null)
            {
                await _graphUpdaterService.UpdateSyncJobStatusAsync(syncJob, request.Status, false, request.SyncJob.RunId.GetValueOrDefault(), request.UsersAdded, request.UsersRemoved);
            }

            _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
        }
    }
}