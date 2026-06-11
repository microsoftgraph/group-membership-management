// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class LogNestedGroupsFunction
    {
        private readonly ILogger<LogNestedGroupsFunction> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public LogNestedGroupsFunction(ILogger<LogNestedGroupsFunction> logger, IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(LogNestedGroupsFunction))]
        public async Task<List<AzureADGroup>> LogNestedGroupsAsync([ActivityTrigger] LogNestedGroupsRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(LogNestedGroupsFunction));

                try
                {
                    var groups = await _graphGroupRepository.GetDirectGroupTypeMembersAsync(request.GroupId);

                    _logger.RetrievedNestedGroups(groups.Count, request.GroupId, string.Join(", ", groups.Select(g => g.ObjectId)));

                    _logger.FunctionCompleted(nameof(LogNestedGroupsFunction));
                    return groups;
                }
                catch (Exception ex)
                {
                    _logger.NestedGroupsRetrievalError(ex, request.GroupId, ex.Message);
                    throw;
                }
            }
        }
    }
}