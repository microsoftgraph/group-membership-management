// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models.Entities;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public JobStatusUpdaterFunction(
                        ILoggingRepository loggingRepository,
                        IGraphUpdaterService graphUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.JobPartitionKey, request.JobRowKey);
            syncJob.ThresholdViolations = request.ThresholdViolations;
            if (request.Status == SyncStatus.Idle && syncJob.IgnoreThresholdOnce) syncJob.IgnoreThresholdOnce = false;

            if (syncJob != null)
            {
                await _graphUpdaterService.UpdateSyncJobStatusAsync(syncJob, request.Status, false, request.RunId);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}