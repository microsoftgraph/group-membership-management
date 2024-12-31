// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Hosts.GraphUpdater
{
    public class GetGroupFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GetGroupFunction(ILoggingRepository loggingRepository, IGraphUpdaterService graphUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [FunctionName(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            _graphUpdaterService.RunId = syncJob.RunId ?? Guid.Empty;
            var groupId = await _graphUpdaterService.GetGroupIdAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return groupId;
        }
    }
}