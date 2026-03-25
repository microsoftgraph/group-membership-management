// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Entities;
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models.SyncJobHistory;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
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
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));

                var now = DateTime.UtcNow;
                var updatedBy = nameof(Hosts.SqlMembershipObtainer);
                var history = new SyncJobHistory
                {
                    SyncJobId = request.SyncJob.Id,
                    RunId = request.SyncJob.RunId ?? Guid.Empty,
                    Status = request.Status.ToString(),
                    UpdatedByFunction = updatedBy,
                    EndTime = request.Status != SyncStatus.InProgress ? now : null,
                    UpdatedAt = now
                };

                request.SyncJob.Status = request.Status.ToString();

                await _syncJobStatusService.UpdateJobStatusAsync(request.SyncJob, request.Status, history, functionName: updatedBy);

                _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
            }
        }
    }
}