// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.ServiceBus
{
    public class JobStatusUpdateQueueMessage
    {
        /// <summary>
        /// The unique identifier for the job run
        /// </summary>
        public required Guid RunId { get; set; }

        /// <summary>
        /// The unique identifier for the sync job
        /// </summary>
        public required Guid JobId { get; set; }

        /// <summary>
        /// The new status to set for the job
        /// </summary>
        public required SyncStatus NewStatus { get; set; }

        /// <summary>
        /// The number of threshold violations for this job
        /// </summary>
        public int? ThresholdViolations { get; set; }

        /// <summary>
        /// The name of the function that triggered this update
        /// </summary>
        public required string UpdatedByFunction { get; set; }

        /// <summary>
        /// Users added count for job history tracking
        /// </summary>
        public int? UsersAddedCount { get; set; }

        /// <summary>
        /// Users removed count for job history tracking
        /// </summary>
        public int? UsersRemovedCount { get; set; }

        /// <summary>
        /// Job start time for duration calculation
        /// </summary>
        public DateTime? JobStartTime { get; set; }

        /// <summary>
        /// Job end time for duration calculation
        /// </summary>
        public DateTime? JobEndTime { get; set; }

        /// Current sync job details
        /// </summary>
        public required SyncJob SyncJob { get; set; }
    }
}
