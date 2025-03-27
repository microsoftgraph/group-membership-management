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

            var groups = await _graphGroupRepository.GetGroupsByFilterAsync("startswith(displayName,'LoadTesting_DestinationGroup_')");
            var existingGroups = groups.ToDictionary(g => g.ObjectId, g => g.Name);
            var existingSyncJobs = request.TargetGroupIds.ToHashSet();
            var missingJobs = existingGroups
                .Where(group => !existingSyncJobs.Contains(group.Key))
                .ToList();

            var groupSizesAndIds = new Dictionary<int, List<Guid>>();
            foreach (var group in missingJobs)
            {
                var groupName = group.Value;
                var groupSize = int.Parse(groupName.Split('_')[2]);
                if (!groupSizesAndIds.ContainsKey(groupSize))
                {
                    groupSizesAndIds[groupSize] = new List<Guid>();
                }

                groupSizesAndIds[groupSize].Add(group.Key);
            }


            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SyncJobCheckerFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return new SyncJobCheckerResponse
            {
                GroupSizesAndIds = groupSizesAndIds
            };
        }
    }
}
