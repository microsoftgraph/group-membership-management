// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Models
{
    public class PendingSyncJobChange
    {
        /// <summary>
        /// Gets or sets an id representing the PendingSyncJobChange.
        /// </summary>
        public Guid Id { get; set; }
        /// <summary>
        /// Gets or sets the id of the syncjob in the SyncJobs table.
        /// </summary>
        public Guid? SyncJobId { get; set; }
        /// <summary>
        /// Gets or sets the UTC date and time of when the change occurred.
        /// The default time is the current UTC date and time.
        /// </summary>
        public DateTime ChangeTime { get; set; }
        /// <summary>
        /// Gets or sets the userId of the responsible for the change.
        /// </summary>
        public Guid RequestorUserId { get; set; }
        /// <summary>
        /// Gets or sets the alias of the responsible for the change.
        /// </summary>
        public string RequestorUPN { get; set; }
        /// <summary>
        /// Gets or sets the review status.
        /// </summary>
        public ReviewStatus Status { get; set; }
        /// <summary>
        /// Gets or sets the details of the change.
        /// </summary>
        /// <remarks>
        /// This is a JSON string that can be deserialized into
        /// a <see cref="SyncJob"/> object.
        /// </remarks>
        public string ChangeDetails { get; set; }
    }
}