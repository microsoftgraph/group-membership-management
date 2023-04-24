// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GroupUpdaterFunction(
            ILoggingRepository loggingRepository,
            IGraphUpdaterService graphUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [FunctionName(nameof(GroupUpdaterFunction))]
        public async Task<GroupUpdaterResponse> UpdateGroupAsync([ActivityTrigger] GroupUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            _graphUpdaterService.RunId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            GraphUpdaterStatus responseStatus;
            var successCount = 0;
            var usersNotFound = new List<AzureADUser>();
            var usersAlreadyExist = new List<AzureADUser>();
            var destination = JsonParser.GetDestination(request.SyncJob.Destination);

            if (request.Type == RequestType.Add)
            {
                var addUsersToGraphResponse = await _graphUpdaterService.AddUsersToGroupAsync(
                    request.Members, destination.TargetGroupId, request.SyncJob.RunId.GetValueOrDefault(), request.IsInitialSync);

                responseStatus = addUsersToGraphResponse.Status;
                successCount = addUsersToGraphResponse.SuccessCount;
                usersNotFound = addUsersToGraphResponse.UsersNotFound;
                usersAlreadyExist = addUsersToGraphResponse.UsersAlreadyExist;
            }
            else
            {
                var removeUsersFromGraphResponse = await _graphUpdaterService.RemoveUsersFromGroupAsync(
                    request.Members, destination.TargetGroupId, request.SyncJob.RunId.GetValueOrDefault(), request.IsInitialSync);

                responseStatus = removeUsersFromGraphResponse.Status;
                successCount = removeUsersFromGraphResponse.SuccessCount;
                usersNotFound = removeUsersFromGraphResponse.UsersNotFound;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

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