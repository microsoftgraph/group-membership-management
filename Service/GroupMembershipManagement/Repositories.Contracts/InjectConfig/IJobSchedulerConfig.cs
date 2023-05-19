// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.InjectConfig
{
    public interface IJobSchedulerConfig
    {
        public bool ResetJobs { get; }
        public int DaysToAddForReset { get; }
        public bool DistributeJobs { get; }
        public bool IncludeFutureJobs { get; }
        public int StartTimeDelayMinutes { get; set; }
        public int DelayBetweenSyncsSeconds { get; }
        public int DefaultRuntimeSeconds { get; }
        public bool GetRunTimeFromLogs { get; }
        public string RunTimeMetric { get; }
        public string RunTimeQuery { get; }
        public int RunTimeRangeInDays { get; }
        public string WorkspaceId { get; }
    }
}
