// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;

        public JobReaderFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository syncJobRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
        }

        [Function(nameof(JobReaderFunction))]
        public async Task<SyncJob> GetSyncJobAsync([ActivityTrigger] JobReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var syncJob = await _syncJobRepository.GetSyncJobAsync(request.JobId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return syncJob;
        }
    }
}
