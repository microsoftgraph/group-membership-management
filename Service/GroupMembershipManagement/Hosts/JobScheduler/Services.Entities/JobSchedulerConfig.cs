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
        public bool IncludeFutureJobs { get; }
        public int StartTimeDelayMinutes { get; }
        public int DelayBetweenSyncsSeconds { get; }
        public int DefaultRuntimeSeconds { get; }

        public JobSchedulerConfig(
            bool resetJobs,
            int daysToAddForReset,
            bool distributeJobs,
            bool includeFutureJobs,
            int startTimeDelayMinutes,
            int delayBetweenSyncsSeconds,
            int defaultRuntimeSeconds)
        {
            ResetJobs = resetJobs;
            DaysToAddForReset = daysToAddForReset;
            DistributeJobs = distributeJobs;
            IncludeFutureJobs = includeFutureJobs;
            StartTimeDelayMinutes = startTimeDelayMinutes;
            DelayBetweenSyncsSeconds = delayBetweenSyncsSeconds;
            DefaultRuntimeSeconds = defaultRuntimeSeconds;
        }
    }
}
