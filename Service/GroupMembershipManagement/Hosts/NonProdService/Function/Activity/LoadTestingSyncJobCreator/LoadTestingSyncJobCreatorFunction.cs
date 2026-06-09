// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models;
using NonProdService.Activity.LoadTestingSyncJobCreator;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Data.SqlTypes;

namespace Hosts.NonProdService
{
    public class LoadTestingSyncJobCreatorFunction
    {
        private readonly ILogger<LoadTestingSyncJobCreatorFunction> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository = null;
        private readonly IOptions<LoadTestingSyncJobCreatorOptions> _options;

        public LoadTestingSyncJobCreatorFunction(ILogger<LoadTestingSyncJobCreatorFunction> logger, IDatabaseSyncJobsRepository databaseSyncJobsRepository, IOptions<LoadTestingSyncJobCreatorOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        [Function(nameof(LoadTestingSyncJobCreatorFunction))]
        public async Task CreateLoadTestingSyncJobs([ActivityTrigger] LoadTestingSyncJobCreatorRequest request)
        {
            var runId = request.RunId;
            var groupSizesAndIds = request.GroupSizesAndIds;
            var targetGroupIds = request.TargetGroupIds;
            var options = _options.Value;

            using (_logger.BeginRunIdScope(runId))
            {
                _logger.FunctionStarted(nameof(LoadTestingSyncJobCreatorFunction));

                var groupSizeCounts = groupSizesAndIds.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
                var syncJobCountsLog = string.Join(", ", groupSizeCounts.Select(kvp => $"{kvp.Key} => {kvp.Value}"));
                _logger.SyncJobsToBeCreated(syncJobCountsLog);

                // spread out jobs evenly across 1 day
                var totalJobsToCreate = groupSizesAndIds.Keys.Sum(groupSize => groupSizesAndIds[groupSize].Count);
                var minutesInADay = 60 * 24;
                var minutesBetweenJobs = minutesInADay / totalJobsToCreate;

                // For ensuring no-op enforcement
                var P = 4; // Thus %no op = 1 - 1/4 = 75%
                var jobIndex = 0;

                var nextJobTime = DateTime.UtcNow;

                foreach (var groupSize in groupSizesAndIds.Keys)
                {
                    foreach (var groupId in groupSizesAndIds[groupSize])
                    {
                        if (targetGroupIds.Any(id => id.ToString().ToLower().Equals(groupId.ToString().ToLower())))
                        {
                            continue;
                        }

                        var destination = "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"" + groupId + "\"}}]";

                        var offset = (int)Math.Ceiling(groupSize * ((decimal)options.SyncJobChangePercent / 100));
                        var offsetProbabilityAsMS = (int)(1000 * ((decimal)options.SyncJobProbabilityOfChangePercent / 100));
                        var filter = $"(EmployeeId > 0 AND EmployeeId <= {groupSize} AND ({jobIndex % (2 * P)} + DATEPART(dayofyear, GETDATE())) % ({2*P}) < {P}) OR (EmployeeId > {offset} AND EmployeeId <= {groupSize + offset} AND ({jobIndex % (2 * P)} + DATEPART(dayofyear, GETDATE())) % ({2 * P}) >= {P})";
                        var query = "[{\"type\":\"SqlMembership\",\"source\":{\"filter\": \"" + filter + "\"}}]";

                        nextJobTime = nextJobTime.AddMinutes(minutesBetweenJobs);

                        var syncJob = new SyncJob
                        {
                            Status = "Idle",
                            Period = 24,
                            Requestor = options.RequestorEmail,
                            TargetOfficeGroupId = groupId,
                            Destination = destination,
                            AllowEmptyDestination = false,
                            ThresholdPercentageForAdditions = 100,
                            ThresholdPercentageForRemovals = 20,
                            ThresholdViolations = 0,
                            StartDate = DateTime.UtcNow,
                            LastRunTime = SqlDateTime.MinValue.Value.AddDays(1),
                            ScheduledDate = nextJobTime,
                            IgnoreThresholdOnce = true,
                            Query = query,
                            MembershipType = MembershipTypes.GroupMembership.ToString(),
                            Group = new Group
                            {
                                GroupId = groupId
                            }
                        };

                        await _databaseSyncJobsRepository.CreateSyncJobAsync(syncJob);

                        jobIndex++;
                    }
                }

                _logger.FunctionCompleted(nameof(LoadTestingSyncJobCreatorFunction));
            }
        }
    }
}
