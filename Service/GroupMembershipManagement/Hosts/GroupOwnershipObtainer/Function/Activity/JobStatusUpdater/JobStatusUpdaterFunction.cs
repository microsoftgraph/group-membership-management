// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public JobStatusUpdaterFunction(
                        ILoggingRepository loggingRepository,
                        ISyncJobStatusService syncJobStatusService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"{nameof(JobStatusUpdaterFunction)} function started",
                    RunId = request.SyncJob.RunId
                }, VerbosityLevel.DEBUG);

            var now = DateTime.UtcNow;
            var updatedBy = "GroupOwnershipObtainer";

            request.SyncJob.Status = request.Status.ToString();
            var history = new SyncJobHistory
            {
                SyncJobId = request.SyncJob.Id,
                RunId = request.SyncJob.RunId ?? Guid.Empty,
                Status = request.Status.ToString(),
                StartTime = request.SyncJob.LastRunTime,
                EndTime = request.Status != SyncStatus.InProgress ? now : null,
                UpdatedByFunction = updatedBy,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _syncJobStatusService.UpdateJobStatusAsync(request.SyncJob, request.Status, history, functionName: updatedBy);

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"{nameof(JobStatusUpdaterFunction)} function completed",
                    RunId = request.SyncJob.RunId
                }, VerbosityLevel.DEBUG);
        }
    }
}
