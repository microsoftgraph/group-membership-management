// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using System.Threading.Tasks;
using Services.Contracts;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.SyncJobUpdater
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobUpdaterService _syncJobUpdaterService;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, ISyncJobUpdaterService syncJobUpdaterService)
        {
            _loggingRepository = loggingRepository;
            _syncJobUpdaterService = syncJobUpdaterService;
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
                await _syncJobUpdaterService.UpdateSyncJobStatusAsync(request.SyncJob, request.Status);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}