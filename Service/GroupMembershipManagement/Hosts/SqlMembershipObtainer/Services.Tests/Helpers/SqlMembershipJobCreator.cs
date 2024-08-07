// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using System;
using System.Collections.Generic;

namespace Services.Tests.Helpers
{
    static class SqlMembershipJobCreator
    {
        public static List<SyncJob> CreateSampleSyncJobs(int numberOfJobs, string syncType, int period = 1, DateTime? startDateBase = null, DateTime? lastRunTime = null)
        {
            var jobs = new List<SyncJob>();

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    Id = Guid.NewGuid(),
                    Period = period,
                    Query = GetJobQuery(syncType, Random.Shared.Next(1000, 10000).ToString()),
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

        public static string GetJobQuery(string syncType, string managerId)
        {
            var individualQueries = $"{{\"type\":\"{syncType}\"," +
                                    $"\"source\": {{\"id\":[{managerId}]," +
                                    $"\"filter\":\"(Attribute = 'Value')\"}} }}";

            return $"[{string.Join(",", individualQueries)}]";

        }
    }
}
