// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class GroupUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GroupUpdaterFunction(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(GroupUpdaterFunction))]
        public async Task<int> UpdateGroupAsync([ActivityTrigger] GroupUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return successCount;
        }
    }
}