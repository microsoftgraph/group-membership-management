// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using Microsoft.Azure.Functions.Worker;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Models.SyncJobHistory;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
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
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var now = DateTime.UtcNow;
            var history = new SyncJobHistory
            {
                SyncJobId = request.SyncJob.Id,
                RunId = request.SyncJob.RunId ?? Guid.Empty,
                Status = request.Status.ToString(),
                UpdatedByFunction = "SqlMembershipObtainer",
                EndTime = request.Status != SyncStatus.InProgress ? now : null,
                UpdatedAt = now
            };

            request.SyncJob.Status = request.Status.ToString();

            await _syncJobStatusService.UpdateJobStatusAsync(request.SyncJob, request.Status, history, functionName: "SqlMembershipObtainer");

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}
