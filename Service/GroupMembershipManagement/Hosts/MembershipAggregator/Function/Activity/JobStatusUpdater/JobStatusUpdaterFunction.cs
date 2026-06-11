// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using Models.SyncJobHistory;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public JobStatusUpdaterFunction(
            ILogger<JobStatusUpdaterFunction> logger,
            IDatabaseSyncJobsRepository syncJobRespository,
            ISyncJobStatusService syncJobStatusService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobRepository = syncJobRespository ?? throw new ArgumentNullException(nameof(syncJobRespository));
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

                var syncJob = await _syncJobRepository.GetSyncJobAsync(request.SyncJob.Id);
                if (syncJob != null)
                {
                    var currentDate = DateTime.UtcNow;
                    if (request.IncrementThresholdViolations)
                        syncJob.ThresholdViolations += 1;

                    if (request.IsDryRun)
                        syncJob.DryRunTimeStamp = currentDate;
                    else
                    {
                        syncJob.LastRunTime = currentDate;

                        if (request.IsNoOpSync)
                        {
                            if (syncJob.IgnoreThresholdOnce) syncJob.IgnoreThresholdOnce = false;

                            syncJob.ThresholdViolations = 0;
                            syncJob.LastSuccessfulRunTime = currentDate;
                        }
                    }

                    syncJob.ScheduledDate = currentDate.AddHours(syncJob.Period);

                    syncJob.Status = request.Status.ToString();
                    syncJob.RunId = syncJob.RunId ?? request.SyncJob.RunId;

                    var history = new SyncJobHistory
                    {
                        SyncJobId = syncJob.Id,
                        RunId = syncJob.RunId ?? request.SyncJob.RunId ?? Guid.Empty,
                        Status = request.Status.ToString(),
                        UpdatedByFunction = "MembershipAggregator",
                        ThresholdViolations = syncJob.ThresholdViolations,
                        EndTime = currentDate,
                        UpdatedAt = currentDate
                    };

                    await _syncJobStatusService.UpdateJobStatusAsync(syncJob, request.Status, history, functionName: "MembershipAggregator");
                }

                _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
            }
        }
    }
}