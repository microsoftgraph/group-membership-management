// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class GetChannelFunction
    {
        private readonly ILogger<GetChannelFunction> _logger;
        private readonly IDeltaCalculatorService _deltaCalculatorService;

        public GetChannelFunction(ILogger<GetChannelFunction> logger, IDeltaCalculatorService deltaCalculatorService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
        }

        [Function(nameof(GetChannelFunction))]
        public async Task<string> GetChannelAsync([ActivityTrigger] GetChannelRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(GetChannelFunction));
                var channelId = request.SyncJob.MembershipType == MembershipTypes.GroupMembership.ToString() ? string.Empty : await _deltaCalculatorService.GetChannelIdAsync(request.SyncJob);
                _logger.FunctionCompleted(nameof(GetChannelFunction));
                return channelId;
            }
        }
    }
}