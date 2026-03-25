// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class GetGroupFunction
    {
        private readonly ILogger<GetGroupFunction> _logger;
        private readonly IDeltaCalculatorService _deltaCalculatorService;

        public GetGroupFunction(ILogger<GetGroupFunction> logger, IDeltaCalculatorService deltaCalculatorService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupAsync([ActivityTrigger] GetGroupRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(GetGroupFunction));
                if (request.SyncJob == null) return Guid.Empty;
                var groupId = await _deltaCalculatorService.GetGroupIdAsync(request.SyncJob);
                _logger.FunctionCompleted(nameof(GetGroupFunction));
                return groupId;
            }
        }
    }
}