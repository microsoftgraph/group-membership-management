// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models;
using NonProdService.Activity.LoadTestingSyncJobCreator;
using Repositories.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Data.SqlTypes;

namespace Hosts.NonProdService
{
    public class LoadTestingSyncJobCreatorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository = null;
        private readonly IOptions<LoadTestingSyncJobCreatorOptions> _options;

        public LoadTestingSyncJobCreatorFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository databaseSyncJobsRepository, IOptions<LoadTestingSyncJobCreatorOptions> options)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        [FunctionName(nameof(LoadTestingSyncJobCreatorFunction))]
        public async Task CreateLoadTestingSyncJobs([ActivityTrigger] LoadTestingSyncJobCreatorRequest request, ILogger log)
        {
            var runId = request.RunId;
            var groupSizesAndIds = request.GroupSizesAndIds;
            var targetGroupIds = request.TargetGroupIds;
            var options = _options.Value;

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingSyncJobCreatorFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingSyncJobCreatorFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}
