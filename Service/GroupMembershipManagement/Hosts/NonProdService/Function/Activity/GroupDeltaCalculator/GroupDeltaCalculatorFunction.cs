// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class GroupDeltaCalculatorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GroupDeltaCalculatorFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(GroupDeltaCalculatorFunction))]
        public async Task<GroupDeltaCalculatorResponse> CalculateGroupDelta([ActivityTrigger] GroupDeltaCalculatorRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupDeltaCalculatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var groupNames = await _graphGroupRepository.GetAllGroupNamesAsync();

            var groupsToCreate = new Dictionary<int, int>();

            foreach (var groupSize in request.GroupSizesAndCounts.Keys)
            {
                var baseGroupName = $"LoadTesting_DestinationGroup_{groupSize}";
                var existingGroupCount = groupNames.Count(name => name.StartsWith(baseGroupName, StringComparison.OrdinalIgnoreCase));
                var requiredGroupCount = request.GroupSizesAndCounts[groupSize];
                var groupsNeeded = requiredGroupCount - existingGroupCount;

                if (groupsNeeded > 0)
                {
                    groupsToCreate[groupSize] = groupsNeeded;
                }
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupDeltaCalculatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return new GroupDeltaCalculatorResponse
            {
                GroupsToCreate = groupsToCreate
            };
        }
    }
}
