// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;

        public JobStatusUpdaterFunction(
                        ILoggingRepository loggingRepository,
                        IDatabaseSyncJobsRepository syncJobRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
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

            await _syncJobRepository.UpdateSyncJobStatusAsync(new List<SyncJob> { request.SyncJob }, request.Status);

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"{nameof(JobStatusUpdaterFunction)} function completed",
                    RunId = request.SyncJob.RunId
                }, VerbosityLevel.DEBUG);
        }
    }
}
