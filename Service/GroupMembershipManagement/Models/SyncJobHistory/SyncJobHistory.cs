// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Models.SyncJobHistory
{
    /// <summary>
    /// Represents the execution history of a sync job
    /// </summary>
    public class SyncJobHistory
    {
        /// <summary>
        /// Gets or sets the unique identifier for the history entry
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the sync job identifier
        /// </summary>
        public Guid SyncJobId { get; set; }

        /// <summary>
        /// Gets or sets the run identifier for this execution
        /// </summary>
        public Guid RunId { get; set; }

        /// <summary>
        /// Gets or sets the start time of the job execution
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// Gets or sets the end time of the job execution
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Gets or sets the duration of the job execution in seconds
        /// </summary>
        public int? Duration { get; set; }

        /// <summary>
        /// Gets or sets the final status of the job execution
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Gets or sets the number of users added during this execution
        /// </summary>
        public int? UsersAdded { get; set; }

        /// <summary>
        /// Gets or sets the number of users removed during this execution
        /// </summary>
        public int? UsersRemoved { get; set; }

        /// <summary>
        /// Gets or sets the number of threshold violations
        /// </summary>
        public int? ThresholdViolations { get; set; }

        /// <summary>
        /// Gets or sets the user count before the sync operation
        /// </summary>
        public int? BeforeSyncUserCount { get; set; }

        /// <summary>
        /// Gets or sets the user count after the sync operation
        /// </summary>
        public int? AfterSyncUserCount { get; set; }

        /// <summary>
        /// Gets or sets the function that updated the job status
        /// </summary>
        public string UpdatedByFunction { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this history entry was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this history entry was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; }

    }
}
