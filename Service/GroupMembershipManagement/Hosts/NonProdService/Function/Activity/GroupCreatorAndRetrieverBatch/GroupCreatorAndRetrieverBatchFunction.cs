// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class GroupCreatorAndRetrieverBatchFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GroupCreatorAndRetrieverBatchFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(GroupCreatorAndRetrieverBatchFunction))]
        public async Task<List<GroupCreatorAndRetrieverBatchResponse>> RunBatchAsync([ActivityTrigger] GroupCreatorAndRetrieverBatchRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var responses = new List<GroupCreatorAndRetrieverBatchResponse>();
            var existingGroups = request.ExistingGroupNames ?? new List<string>();
            var existingGroupCount = existingGroups
                .Count(name => name.StartsWith(request.BaseGroupName + "_", StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < request.GroupCount; i++)
            {
                var groupName = $"{request.BaseGroupName}_{existingGroupCount + i + 1}";

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupCreatorAndRetrieverBatchFunction)} creating group {groupName}", RunId = request.RunId }, VerbosityLevel.DEBUG);

                await _graphGroupRepository.CreateGroup(groupName, request.TestGroupType, request.GroupOwnersIds);

                var group = await _graphGroupRepository.GetGroup(groupName);

                if (group == null)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupCreatorAndRetrieverBatchFunction)} failed to create group {groupName}. Retrying...", RunId = request.RunId });

                    var attempts = 0;
                    while (group == null && attempts < 5)
                    {
                        attempts++;
                        await Task.Delay(5000);
                        group = await _graphGroupRepository.GetGroup(groupName);
                    }

                    if (group == null)
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupCreatorAndRetrieverBatchFunction)} failed to create group {groupName} after multiple attempts", RunId = request.RunId });
                        continue;
                    }
                }

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Successfully created group with name {groupName}.", RunId = request.RunId });

                var usersInGroup = request.RetrieveMembers ? await _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId) : null;

                responses.Add(new GroupCreatorAndRetrieverBatchResponse
                {
                    TargetGroup = group,
                    Members = usersInGroup
                });
            }

            return responses;
        }
    }
}
