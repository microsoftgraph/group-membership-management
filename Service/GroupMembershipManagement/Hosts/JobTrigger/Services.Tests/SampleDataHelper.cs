// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.Tests
{
    static class SampleDataHelper
    {
        public static List<SyncJob> CreateSampleSyncJobs(int numberOfJobs, string syncType, int period = 1, DateTime? startDateBase = null, DateTime? lastRunTime = null)
        {
            var jobs = new List<SyncJob>();

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                    RowKey = Guid.NewGuid().ToString(),
                    Period = period,
                    Query = GetJobQuery(syncType, new[] { Guid.NewGuid().ToString() }),
                    StartDate = startDateBase ?? DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = lastRunTime ?? DateTime.FromFileTimeUtc(0),
                    RunId = Guid.NewGuid()
                };

                jobs.Add(job);
            }

            return jobs;
        }

        public static string GetJobQuery(string syncType, string[] groupIds)
        {
            var individualQueries = groupIds.Select(x => $"{{\"type\":\"{syncType}\",\"source\": \"{x}\"}}");
            var query = $"[{string.Join(",", individualQueries)}]";
            return query;
        }
    }
}
