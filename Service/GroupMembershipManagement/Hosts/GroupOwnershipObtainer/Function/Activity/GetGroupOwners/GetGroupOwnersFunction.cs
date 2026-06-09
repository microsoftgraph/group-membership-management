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
    public class GetGroupOwnersFunction
    {
        private readonly ILogger<GetGroupOwnersFunction> _logger;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService;

        public GetGroupOwnersFunction(ILogger<GetGroupOwnersFunction> logger, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [Function(nameof(GetGroupOwnersFunction))]
        public async Task<List<Guid>> GetGroupOwnersAsync([ActivityTrigger] GetGroupOwnersRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });
            _logger.FunctionStarted(nameof(GetGroupOwnersFunction));
            var ids = await _groupOwnershipObtainerService.GetGroupOwnersAsync(request.GroupId);
            _logger.FunctionCompleted(nameof(GetGroupOwnersFunction));

            return ids;
        }
    }
}
