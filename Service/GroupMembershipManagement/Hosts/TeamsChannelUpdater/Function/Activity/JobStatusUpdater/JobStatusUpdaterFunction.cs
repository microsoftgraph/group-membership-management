// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public JobStatusUpdaterFunction(
                        ILoggingRepository loggingRepository,
                        ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var syncJob = await _teamsChannelUpdaterService.GetSyncJobAsync(request.JobPartitionKey, request.JobRowKey);
            syncJob.ThresholdViolations = request.ThresholdViolations;
            if (request.Status == SyncStatus.Idle && syncJob.IgnoreThresholdOnce) syncJob.IgnoreThresholdOnce = false;

            if (syncJob != null)
            {
                await _teamsChannelUpdaterService.UpdateSyncJobStatusAsync(syncJob, request.Status, false, request.RunId);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}