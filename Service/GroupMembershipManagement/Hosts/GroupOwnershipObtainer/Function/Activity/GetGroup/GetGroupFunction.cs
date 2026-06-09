// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class GetGroupFunction
    {
        private readonly ILogger<GetGroupFunction> _logger;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService;

        public GetGroupFunction(ILogger<GetGroupFunction> logger, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupAsync([ActivityTrigger] GetGroupRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });
            _logger.FunctionStarted(nameof(GetGroupFunction));
            var groupId = await _groupOwnershipObtainerService.GetGroupIdAsync(request.SyncJob);
            _logger.FunctionCompleted(nameof(GetGroupFunction));
            return groupId;
        }
    }
}