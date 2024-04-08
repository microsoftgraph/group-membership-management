// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace Services.Contracts
{
    public class JobSchedulerConfig : IJobSchedulerConfig
    {
        public bool ResetJobs { get; }
        public int DaysToAddForReset { get; }
        public bool DistributeJobs { get; }
        public int StartTimeDelayMinutes { get; set; }
        public int DelayBetweenSyncsSeconds { get; }
        public int DefaultRuntimeSeconds { get; }
        public bool GetRunTimeFromLogs { get; set; }
        public string RunTimeMetric { get; set; }
        public string RunTimeQuery { get; set; }
        public int RunTimeRangeInDays { get; set; }
        public string WorkspaceId { get; set; }

        public JobSchedulerConfig(
            bool resetJobs,
            int daysToAddForReset,
            bool distributeJobs,
            int startTimeDelayMinutes,
            int delayBetweenSyncsSeconds,
            int defaultRuntimeSeconds,
            bool getRunTimeFromLogs,
            string runTimeMetric,
            string runTimeQuery,
            int runTimeRangeInDays,
            string workspaceId
            )
        {
            ResetJobs = resetJobs;
            DaysToAddForReset = daysToAddForReset;
            DistributeJobs = distributeJobs;
            StartTimeDelayMinutes = startTimeDelayMinutes;
            DelayBetweenSyncsSeconds = delayBetweenSyncsSeconds;
            DefaultRuntimeSeconds = defaultRuntimeSeconds;
            GetRunTimeFromLogs = getRunTimeFromLogs;
            RunTimeMetric = runTimeMetric;
            RunTimeQuery = runTimeQuery;
            RunTimeRangeInDays = runTimeRangeInDays;
            WorkspaceId = workspaceId;
        }
    }
}
