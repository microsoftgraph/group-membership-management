// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.OwnershipReader
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobRepository _syncJobRepository;

        public JobStatusUpdaterFunction(
                        ILoggingRepository loggingRepository,
                        ISyncJobRepository syncJobRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
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
