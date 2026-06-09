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

namespace Hosts.NonProdService
{
    public class GroupCreatorAndRetrieverBatchFunction
    {
        private readonly ILogger<GroupCreatorAndRetrieverBatchFunction> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GroupCreatorAndRetrieverBatchFunction(ILogger<GroupCreatorAndRetrieverBatchFunction> logger, IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(GroupCreatorAndRetrieverBatchFunction))]
        public async Task<List<GroupCreatorAndRetrieverBatchResponse>> RunBatchAsync([ActivityTrigger] GroupCreatorAndRetrieverBatchRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(GroupCreatorAndRetrieverBatchFunction));

                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                var responses = new List<GroupCreatorAndRetrieverBatchResponse>();
                var existingGroups = request.ExistingGroupNames ?? new List<string>();
                var existingGroupSet = new HashSet<string>(existingGroups, StringComparer.OrdinalIgnoreCase);
                var existingGroupCount = existingGroups
                    .Count(name => name.StartsWith(request.BaseGroupName + "_", StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < request.GroupCount; i++)
                {
                    var groupName = $"{request.BaseGroupName}_{existingGroupCount + request.StartingIndex + i + 1}";

                    if (existingGroupSet.Contains(groupName))
                    {
                        _logger.SkippingExistingGroup(groupName);
                        continue;
                    }

                    _logger.CreatingGroup(nameof(GroupCreatorAndRetrieverBatchFunction), groupName);

                    var group = await _graphGroupRepository.CreateGroup(groupName, request.TestGroupType);

                    if (group == null)
                    {
                        _logger.FailedToCreateGroup(groupName);
                        continue;
                    }

                    _logger.GroupCreatedSuccessfully(groupName);

                    var usersInGroup = request.RetrieveMembers ? await _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId) : null;

                    responses.Add(new GroupCreatorAndRetrieverBatchResponse
                    {
                        TargetGroup = group,
                        Members = usersInGroup
                    });
                }

                _logger.FunctionCompleted(nameof(GroupCreatorAndRetrieverBatchFunction));

                return responses;
            }
        }
    }
}
