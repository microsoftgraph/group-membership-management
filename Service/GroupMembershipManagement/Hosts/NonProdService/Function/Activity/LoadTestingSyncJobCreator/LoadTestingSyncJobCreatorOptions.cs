// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace NonProdService.Activity.LoadTestingSyncJobCreator
{
    public class LoadTestingSyncJobCreatorOptions
    {
        /// <summary>
        /// The number of load testing sync jobs to create.
        /// </summary>
        public int JobCount { get; set; }
        /// <summary>
        /// This is the email address that will be used when creating load testing sync jobs.
        /// </summary>
        public string RequestorEmail { get; set; }
        /// <summary>
        /// The percentage of users that should be shifted into and out of the valid
        /// EmployeeId range.  For example, if this is set to 10, then the job configured
        /// to add 50 users to a group, might included User Ids 1 to 50, or 5 to 55.
        /// </summary>
        public int SyncJobChangePercent { get; set; }
        /// <summary>
        /// The probability that the job will use the <see cref="SyncJobChangePercent"/>
        /// to implement a change.  For example, if this is set to 30, then the job should use
        /// the <see cref="SyncJobChangePercent"/> for 30% of the job runs.
        /// </summary>
        public int SyncJobProbabilityOfChangePercent { get; set; }
    }
}
