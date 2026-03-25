// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class GroupNameReaderFunction
    {
        private readonly ILogger<GroupNameReaderFunction> _logger;
        private readonly IGraphAPIService _graphAPIService;

        public GroupNameReaderFunction(ILogger<GroupNameReaderFunction> logger, IGraphAPIService graphAPIService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
        }

        [Function(nameof(GroupNameReaderFunction))]
        public async Task<SyncJobGroup> GetGroupNameAsync([ActivityTrigger] GroupNameReaderRequest request)
        {
            var group = new SyncJobGroup();

            if (request.SyncJob != null)
            {
                using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
                {
                    ["CurrentPart"] = request.CurrentPart,
                    ["TotalParts"] = request.TotalParts
                }))
                {
                    _logger.FunctionStarted(nameof(GroupNameReaderFunction));
                    var groupName = await _graphAPIService.GetGroupNameAsync(request.GroupId);
                    group.SyncJob = request.SyncJob;
                    group.Name = groupName;
                    _logger.FunctionCompleted(nameof(GroupNameReaderFunction));
                }
            }
            return group;
        }
    }
}