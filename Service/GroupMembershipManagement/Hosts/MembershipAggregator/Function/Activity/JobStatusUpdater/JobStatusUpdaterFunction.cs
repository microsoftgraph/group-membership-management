// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository syncJobRespository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRespository ?? throw new ArgumentNullException(nameof(syncJobRespository));
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var syncJob = await _syncJobRepository.GetSyncJobAsync(request.SyncJob.Id);
            if (syncJob != null)
            {
                var currentDate = DateTime.UtcNow;
                if (request.ThresholdViolations.HasValue)
                    syncJob.ThresholdViolations = request.ThresholdViolations.Value;

                if (request.IsDryRun)
                    syncJob.DryRunTimeStamp = currentDate;
                else
                {
                    syncJob.LastRunTime = currentDate;

                    if (request.DeltaStatus == Services.Entities.MembershipDeltaStatus.NoChanges)
                    {
                        if (syncJob.IgnoreThresholdOnce) syncJob.IgnoreThresholdOnce = false;

                        syncJob.LastSuccessfulRunTime = currentDate;
                    }
                }

                syncJob.ScheduledDate = currentDate.AddHours(syncJob.Period);

                await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob }, request.Status);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}