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
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function started", RunId = request.RunId });

            var successCount = 0;

            if (request.Type == RequestType.Add)
            {
                var response = await _graphUpdaterService.AddUsersToGroupAsync(request.Members, request.DestinationGroupId, request.RunId, request.IsInitialSync);
                successCount = response.SuccessCount;
            }
            else
            {
                var response = await _graphUpdaterService.RemoveUsersFromGroupAsync(request.Members, request.DestinationGroupId, request.RunId, request.IsInitialSync);
                successCount = response.SuccessCount;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function completed", RunId = request.RunId });

            return successCount;
        }
    }
}