// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models
{
    /// <summary>
    /// Base row of the consolidated Table-Per-Type (TPT) destination model.
    /// One row exists per supported <see cref="SyncJob"/>. The typed
    /// <see cref="DestinationType"/> reference identifies which per-type
    /// destination (<see cref="GroupDestination"/> or
    /// <see cref="TeamsChannelDestination"/>) belongs to this base record.
    /// </summary>
    public class Destination
    {
        /// <summary>
        /// Primary key and foreign key to <c>SyncJobs.Id</c>. Cascade-deleted
        /// with the owning SyncJob. Exactly one base row exists per supported job.
        /// </summary>
        public Guid SyncJobId { get; set; }

        /// <summary>
        /// Required destination type, stored as the <see cref="MembershipTypes"/> name
        /// (e.g. "GroupMembership"/"TeamsChannelMembership") and foreign-keyed to
        /// <c>MembershipTypes.Name</c> at the database level — the same by-Name reference
        /// standard used by <c>SyncJobs.MembershipType</c>, rather than referencing by Id.
        /// </summary>
        public string DestinationType { get; set; }
    }
}
