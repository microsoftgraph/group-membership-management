// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterFunction
    {
        private readonly ILogger<GroupUpdaterFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GroupUpdaterFunction(
            ILogger<GroupUpdaterFunction> logger,
            IGraphUpdaterService graphUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [Function(nameof(GroupUpdaterFunction))]
        public async Task<GroupUpdaterResponse> UpdateGroupAsync([ActivityTrigger] GroupUpdaterRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(GroupUpdaterFunction));

            GraphUpdaterStatus responseStatus;
            var successCount = 0;
            var usersNotFound = new List<AzureADUser>();
            var usersAlreadyExist = new List<AzureADUser>();
            var destination = JsonParser.GetDestination(request.SyncJob);

            if (request.Type == RequestType.Add)
            {
                var addUsersToGraphResponse = await _graphUpdaterService.AddUsersToGroupAsync(
                    request.Members, destination.ObjectId, request.SyncJob.RunId.GetValueOrDefault(), request.IsInitialSync);

                responseStatus = addUsersToGraphResponse.Status;
                successCount = addUsersToGraphResponse.SuccessCount;
                usersNotFound = addUsersToGraphResponse.UsersNotFound;
                usersAlreadyExist = addUsersToGraphResponse.UsersAlreadyExist;
            }
            else
            {
                var removeUsersFromGraphResponse = await _graphUpdaterService.RemoveUsersFromGroupAsync(
                    request.Members, destination.ObjectId, request.SyncJob.RunId.GetValueOrDefault(), request.IsInitialSync);

                responseStatus = removeUsersFromGraphResponse.Status;
                successCount = removeUsersFromGraphResponse.SuccessCount;
                usersNotFound = removeUsersFromGraphResponse.UsersNotFound;
            }

            _logger.FunctionCompleted(nameof(GroupUpdaterFunction));

            return new GroupUpdaterResponse()
                {
                    Status = responseStatus,
                    SuccessCount = successCount,
                    UsersNotFound = usersNotFound,
                    UsersAlreadyExist = usersAlreadyExist
                };
        }
    }
}