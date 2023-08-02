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
    public class JobReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public JobReaderFunction(ILoggingRepository loggingRepository, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [FunctionName(nameof(JobReaderFunction))]
        public async Task<SyncJob> GetSyncJobAsync([ActivityTrigger] JobReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var syncJob = await _teamsChannelUpdaterService.GetSyncJobAsync(request.JobId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return syncJob;
        }
    }
}
