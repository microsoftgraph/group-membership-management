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
        private readonly IDatabaseSyncJobsRepository _syncJobrespository;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository syncJobRespository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobrespository = syncJobRespository ?? throw new ArgumentNullException(nameof(syncJobRespository));
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var syncJob = await _syncJobrespository.GetSyncJobAsync(request.SyncJob.Id);
            if (syncJob != null)
            {
                if (request.ThresholdViolations.HasValue)
                    syncJob.ThresholdViolations = request.ThresholdViolations.Value;

                if (request.IsDryRun)
                    syncJob.DryRunTimeStamp = DateTime.UtcNow;
                else
                    syncJob.LastRunTime = DateTime.UtcNow;

                await _syncJobrespository.UpdateSyncJobStatusAsync(syncJob, request.Status);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}