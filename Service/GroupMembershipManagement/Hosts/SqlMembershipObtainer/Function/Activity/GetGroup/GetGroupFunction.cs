// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class GetGroupFunction
    {
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public GetGroupFunction(ISqlMembershipObtainerService sqlMembershipObtainerService, ILoggingRepository loggingRepository)
        {
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            var groupId = await _sqlMembershipObtainerService.GetGroupIdAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return groupId;
        }
    }
}