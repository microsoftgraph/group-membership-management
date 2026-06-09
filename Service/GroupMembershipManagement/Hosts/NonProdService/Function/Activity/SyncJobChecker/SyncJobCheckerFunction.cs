// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class SyncJobCheckerFunction
    {
        private readonly ILogger<SyncJobCheckerFunction> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public SyncJobCheckerFunction(ILogger<SyncJobCheckerFunction> logger, IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(SyncJobCheckerFunction))]
        public async Task<SyncJobCheckerResponse> CheckForMissingSyncJobsAsync([ActivityTrigger] SyncJobCheckerRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(SyncJobCheckerFunction));

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

                        _logger.SyncJobCheckerGroupStats(groupSize, expectedCount, groups.Count, numberOfGroupsWithJobs, missing.Count);
                    }

                }

                _logger.FunctionCompleted(nameof(SyncJobCheckerFunction));

                return new SyncJobCheckerResponse
                {
                    GroupSizesAndIds = groupSizesAndIds
                };
            }
        }
    }
}
