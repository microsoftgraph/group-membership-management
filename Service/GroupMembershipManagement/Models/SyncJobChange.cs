// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Models
{
    public class SyncJobChange
    {
        /// <summary>
        /// Gets or sets the id of the syncjob in the SyncJobs table.
        /// </summary>
        public Guid SyncJobId { get; set; }
        /// <summary>
        /// Gets or sets the UTC date and time of when the change occurred.
        /// The default time is the current UTC date and time.
        /// </summary>
        public DateTime ChangeTime { get; set; }
        /// <summary>
        /// Gets or sets the name of service principal responsible for the change.
        /// </summary>
        public string ChangedBy { get; set; }
        /// <summary>
        /// Gets or sets the location where the change originated.
        /// </summary>
        public SyncJobChangeSource ChangeSource { get; set; }
        /// <summary>
        /// Gets or sets the reason for the change.
        /// </summary>
        public string ChangeReason {get;set;}
        /// <summary>
        /// Gets or sets the details of the change.
        /// </summary>
        /// <remarks>
        /// This will likely be a JSON string that can be deserialized into
        /// a <see cref="SyncJob"/> object, but depending on when the change
        /// occurred, the serialized data may not match the current schema.
        /// </remarks>
        public string ChangeDetails { get; set; }
    }
}