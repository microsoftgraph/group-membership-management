// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class GroupCreatorAndRetrieverFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GroupCreatorAndRetrieverFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(GroupCreatorAndRetrieverFunction))]
        public async Task<GroupCreatorAndRetrieverResponse> GenerateGroup([ActivityTrigger] GroupCreatorAndRetrieverRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupCreatorAndRetrieverFunction)} function started", RunId = request.RunId });

            await _graphGroupRepository.CreateGroup(request.GroupName);

            var group = await _graphGroupRepository.GetGroup(request.GroupName);

            var attempts = 0;
            while(group == null && attempts < 5)
            {
                group = await _graphGroupRepository.GetGroup(request.GroupName);
                await Task.Delay(5000);
            }

            if(group == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupCreatorAndRetrieverFunction)} function failed because group couldn't be generated", RunId = request.RunId });

                return null;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Successfully created group with name {request.GroupName}, if it did not exist already", RunId = request.RunId });

            var usersInGroup = await _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupCreatorAndRetrieverFunction)} function completed", RunId = request.RunId });

            return new GroupCreatorAndRetrieverResponse
            {
                TargetGroup = group,
                Members = usersInGroup
            };
        }
    }
}
