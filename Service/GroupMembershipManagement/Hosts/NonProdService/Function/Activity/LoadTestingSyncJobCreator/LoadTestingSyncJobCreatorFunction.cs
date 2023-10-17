// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class LoadTestingSyncJobCreatorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository = null;

        public LoadTestingSyncJobCreatorFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository databaseSyncJobsRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        [FunctionName(nameof(LoadTestingSyncJobCreatorFunction))]
        public async Task CreateLoadTestingSyncJobs([ActivityTrigger] LoadTestingSyncJobCreatorRequest request, ILogger log)
        {
            var runId = request.RunId;
            var groupSizesAndIds = request.GroupSizesAndIds;
            var syncJobs = request.SyncJobs;

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingSyncJobCreatorFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            foreach (var groupSize in groupSizesAndIds.Keys)
            {
                foreach(var groupId in groupSizesAndIds[groupSize])
                {
                    if (syncJobs.Any(syncJob => syncJob.Destination.ToLower().Contains(groupId.ToString().ToLower()))) {
                        continue;
                    }

                    var destination = "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"" + groupId + "\"}}]";

                    var filter = $"EmployeeId <= {groupSize}";
                    var query = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\": \"" + filter + "\"}}]";

                    var syncJob = new SyncJob
                    {
                        Status = "Idle",
                        Period = 24,
                        Requestor = "",
                        TargetOfficeGroupId = groupId,
                        Destination = destination,
                        AllowEmptyDestination = false,
                        ThresholdPercentageForAdditions = 100,
                        ThresholdPercentageForRemovals = 20,
                        ThresholdViolations = 0,
                        Query = query
                    };

                    await _databaseSyncJobsRepository.CreateSyncJobAsync(syncJob);
                }
            }
            
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingSyncJobCreatorFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}
