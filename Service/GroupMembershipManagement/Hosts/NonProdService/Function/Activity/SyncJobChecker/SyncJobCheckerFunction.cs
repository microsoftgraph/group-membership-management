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
    public class SyncJobCheckerFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public SyncJobCheckerFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(SyncJobCheckerFunction))]
        public async Task<SyncJobCheckerResponse> CheckForMissingSyncJobsAsync([ActivityTrigger] SyncJobCheckerRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SyncJobCheckerFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var groupObjects = await _graphGroupRepository.GetGroupsByFilterAsync("startswith(displayName,'LoadTesting_DestinationGroup_')");
            var existingGroups = groupObjects.ToDictionary(g => g.ObjectId, g => g.Name);
            var existingSyncJobs = request.TargetGroupIds.ToHashSet();
            var groupSizesAndIds = new Dictionary<int, List<Guid>>();

            foreach (var kvp in request.ExpectedTargetDistribution)
            {
                var groupSize = kvp.Key;
                var expectedCount = kvp.Value;

                var groups = existingGroups
                    .Where(g => g.Value.Contains($"LoadTesting_DestinationGroup_{groupSize}_"))
                    .Select(g => new { g.Key, g.Value })
                    .ToList();

                var numberOfGroupsWithJobs = groups
                    .Where(g => existingSyncJobs.Contains(g.Key))
                    .ToList()
                    .Count;
                
                if (numberOfGroupsWithJobs < expectedCount)
                {
                    var missingCount = expectedCount - numberOfGroupsWithJobs;
                    var missing = groups
                        .Where(g => !existingSyncJobs.Contains(g.Key))
                        .Take(missingCount) // only take up to the expected count
                        .ToList();
                    groupSizesAndIds[groupSize] = missing.Select(m => m.Key).ToList();

                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Group size: {groupSize}, Expected Group Count: {expectedCount}, Existing Group Count: {groups.Count}, Existing Job Count: {numberOfGroupsWithJobs}, Missing Job Count: {missing.Count}", RunId = request.RunId }, VerbosityLevel.DEBUG);
                }

            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SyncJobCheckerFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return new SyncJobCheckerResponse
            {
                GroupSizesAndIds = groupSizesAndIds
            };
        }
    }
}
