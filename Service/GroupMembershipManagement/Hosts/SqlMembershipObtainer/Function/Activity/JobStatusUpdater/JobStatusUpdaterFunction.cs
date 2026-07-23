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

                if (!request.Status.HasValue)
                {
                    var runId = request.SyncJob.RunId ?? Guid.Empty;
                    if (runId == Guid.Empty)
                    {
                        _logger.LogWarning(
                            "Cannot persist AdfRunId {AdfRunId} for job {SyncJobId}: run has no RunId.",
                            request.AdfRunId, request.SyncJob.Id);
                    }
                    else
                    {
                        var affected = await _syncJobStatusService.SaveAdfRunIdAsync(runId, request.AdfRunId);
                        if (request.AdfRunId.HasValue && affected == 0)
                        {
                            _logger.LogWarning(
                                "AdfRunId {AdfRunId} was not persisted for run {RunId} (job {SyncJobId}): " +
                                "no SyncJobHistory row exists for the run. JobTrigger is expected to create it " +
                                "at claim time.",
                                request.AdfRunId, runId, request.SyncJob.Id);
                        }
                    }

                    _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
                    return;
                }

                var status = request.Status.Value;
                var history = new SyncJobHistory
                {
                    SyncJobId = request.SyncJob.Id,
                    RunId = request.SyncJob.RunId ?? Guid.Empty,
                    Status = status.ToString(),
                    UpdatedByFunction = updatedBy,
                    EndTime = status != SyncStatus.InProgress ? now : null,
                    AdfRunId = request.AdfRunId,
                    UpdatedAt = now
                };

                request.SyncJob.Status = status.ToString();

                await _syncJobStatusService.UpdateJobStatusAsync(request.SyncJob, status, history, functionName: updatedBy);

                _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
            }
        }
    }
}