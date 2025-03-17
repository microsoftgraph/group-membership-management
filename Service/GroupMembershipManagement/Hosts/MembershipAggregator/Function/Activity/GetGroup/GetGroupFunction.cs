// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class GetGroupFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDeltaCalculatorService _deltaCalculatorService = null;

        public GetGroupFunction(ILoggingRepository loggingRepository, IDeltaCalculatorService deltaCalculatorService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
        }

        [FunctionName(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            _deltaCalculatorService.RunId = syncJob.RunId ?? Guid.Empty;
            if (syncJob == null) return Guid.Empty;
            var groupId = await _deltaCalculatorService.GetGroupIdAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return groupId;
        }
    }
}