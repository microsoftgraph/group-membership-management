// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services;
using System;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class GetGroupFunction
    {
        private readonly ILogger<GetGroupFunction> _logger;
        private readonly PlaceMembershipObtainerService _membershipProviderService;

        public GetGroupFunction(ILogger<GetGroupFunction> logger, PlaceMembershipObtainerService membershipProviderService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupAsync([ActivityTrigger] SyncJob syncJob)
        {
            using (_logger.BeginSyncJobScope(syncJob))
            {
                _logger.FunctionStarted(nameof(GetGroupFunction));
                var groupId = await _membershipProviderService.GetGroupIdAsync(syncJob);
                _logger.FunctionCompleted(nameof(GetGroupFunction));
                return groupId;
            }
        }
    }
}