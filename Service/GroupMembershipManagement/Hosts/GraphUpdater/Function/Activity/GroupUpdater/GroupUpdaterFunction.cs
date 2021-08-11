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
        private readonly IGroupUpdaterService _groupUpdaterService;

        public GroupUpdaterFunction(
            ILoggingRepository loggingRepository,
            IGroupUpdaterService groupUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _groupUpdaterService = groupUpdaterService ?? throw new ArgumentNullException(nameof(groupUpdaterService));
        }

        [FunctionName(nameof(GroupUpdaterFunction))]
        public async Task UpdateGroupAsync([ActivityTrigger] GroupUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function started", RunId = request.RunId });

            if (request.Type == RequestType.Add)
                await _groupUpdaterService.AddUsersToGroupAsync(request.Members, request.DestinationGroupId, request.RunId);
            else
                await _groupUpdaterService.RemoveUsersFromGroupAsync(request.Members, request.DestinationGroupId, request.RunId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function completed", RunId = request.RunId });
        }
    }
}