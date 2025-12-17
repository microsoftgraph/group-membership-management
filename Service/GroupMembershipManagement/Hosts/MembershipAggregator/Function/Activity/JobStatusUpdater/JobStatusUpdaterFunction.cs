// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Models.SyncJobHistory;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public JobStatusUpdaterFunction(
            ILoggingRepository loggingRepository,
            IDatabaseSyncJobsRepository syncJobRespository,
            ISyncJobStatusService syncJobStatusService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRespository ?? throw new ArgumentNullException(nameof(syncJobRespository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}