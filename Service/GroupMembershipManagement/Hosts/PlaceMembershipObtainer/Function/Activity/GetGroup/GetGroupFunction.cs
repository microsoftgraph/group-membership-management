// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services;
using System;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class GetGroupFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly PlaceMembershipObtainerService _membershipProviderService = null;

        public GetGroupFunction(ILoggingRepository loggingRepository, PlaceMembershipObtainerService membershipProviderService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            var groupId = await _membershipProviderService.GetGroupIdAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return groupId;
        }
    }
}