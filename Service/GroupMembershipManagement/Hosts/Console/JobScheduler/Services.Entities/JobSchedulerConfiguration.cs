using System;
using System.Collections.Generic;
using System.Text;

namespace Services.Entities
{
    public class JobSchedulerConfiguration
    {
        public bool ResetJobs { get; }
        public bool DistributeJobs { get; }
        public bool IncludeFutureJobs { get; }

        public JobSchedulerConfiguration(bool resetJobs, bool distributeJobs, bool includeFutureJobs)
        {
            ResetJobs = resetJobs;
            DistributeJobs = distributeJobs;
            IncludeFutureJobs = includeFutureJobs;
        }
    }
}
