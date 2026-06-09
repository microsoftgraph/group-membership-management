// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Models.SyncJobHistory;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public JobStatusUpdaterFunction(
                        ILogger<JobStatusUpdaterFunction> logger,
                        ISyncJobStatusService syncJobStatusService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });
            _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));

            var now = DateTime.UtcNow;
            var updatedBy = nameof(Hosts.GroupOwnershipObtainer);

            request.SyncJob.Status = request.Status.ToString();
            var history = new SyncJobHistory
            {
                SyncJobId = request.SyncJob.Id,
                RunId = request.SyncJob.RunId ?? Guid.Empty,
                Status = request.Status.ToString(),
                EndTime = request.Status != SyncStatus.InProgress ? now : null,
                UpdatedByFunction = updatedBy,
                UpdatedAt = now
            };

            await _syncJobStatusService.UpdateJobStatusAsync(request.SyncJob, request.Status, history, functionName: updatedBy);

            _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
        }
    }
}
