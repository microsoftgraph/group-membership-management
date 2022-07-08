// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
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
        public async Task<int> UpdateGroupAsync([ActivityTrigger] GroupUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var successCount = 0;

            if (request.Type == RequestType.Add)
            {
                var addUsersToGraphResponse = await _graphUpdaterService.AddUsersToGroupAsync(
                    request.Members, request.SyncJob.TargetOfficeGroupId, request.SyncJob.RunId.GetValueOrDefault(), request.IsInitialSync);

                successCount = addUsersToGraphResponse.SuccessCount;
            }
            else
            {
                var removeUsersFromGraphResponse = await _graphUpdaterService.RemoveUsersFromGroupAsync(
                    request.Members, request.SyncJob.TargetOfficeGroupId, request.SyncJob.RunId.GetValueOrDefault(), request.IsInitialSync);

                successCount = removeUsersFromGraphResponse.SuccessCount;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            return successCount;
        }
    }
}