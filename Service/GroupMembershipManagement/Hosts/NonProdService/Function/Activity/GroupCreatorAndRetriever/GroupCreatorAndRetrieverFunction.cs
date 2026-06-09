// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class GroupCreatorAndRetrieverFunction
    {
        private readonly ILogger<GroupCreatorAndRetrieverFunction> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GroupCreatorAndRetrieverFunction(ILogger<GroupCreatorAndRetrieverFunction> logger, IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(GroupCreatorAndRetrieverFunction))]
        public async Task<GroupCreatorAndRetrieverResponse> GenerateGroup([ActivityTrigger] GroupCreatorAndRetrieverRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(GroupCreatorAndRetrieverFunction));

                var group = await _graphGroupRepository.CreateGroup(request.GroupName, request.TestGroupType);

                if (group == null)
                {
                    _logger.GroupCouldNotBeGenerated(nameof(GroupCreatorAndRetrieverFunction));

                    return null;
                }

                _logger.SuccessfullyCreatedGroup(request.GroupName);

                var usersInGroup = request.RetrieveMembers ? await _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId) : null;

                _logger.FunctionCompleted(nameof(GroupCreatorAndRetrieverFunction));

                return new GroupCreatorAndRetrieverResponse
                {
                    TargetGroup = group,
                    Members = usersInGroup
                };
            }
        }
    }
}
