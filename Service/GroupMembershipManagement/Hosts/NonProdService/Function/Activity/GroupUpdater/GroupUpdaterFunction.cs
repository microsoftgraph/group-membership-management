// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class GroupUpdaterFunction
    {
        private readonly ILogger<GroupUpdaterFunction> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GroupUpdaterFunction(
            ILogger<GroupUpdaterFunction> logger,
            IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(GroupUpdaterFunction))]
        public async Task<int> UpdateGroupAsync([ActivityTrigger] GroupUpdaterRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(GroupUpdaterFunction));

                var successCount = 0;

                if (request.Type == RequestType.Add)
                {
                    var addUsersToGraphResponse = await _graphGroupRepository.AddUsersToGroup(request.Members, request.TargetGroup);

                    successCount = addUsersToGraphResponse.SuccessCount;
                }
                else
                {
                    var removeUsersFromGraphResponse = await _graphGroupRepository.RemoveUsersFromGroup(request.Members, request.TargetGroup);

                    successCount = removeUsersFromGraphResponse.SuccessCount;
                }

                _logger.FunctionCompleted(nameof(GroupUpdaterFunction));

                return successCount;
            }
        }
    }
}