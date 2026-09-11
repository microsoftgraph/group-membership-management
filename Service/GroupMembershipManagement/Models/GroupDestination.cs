// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models
{
    /// <summary>
    /// Per-type destination for a group SyncJob. Shares its primary key
    /// (<see cref="SyncJobId"/>) with the base <see cref="Destination"/> row
    /// (Table-Per-Type shared key). Name and email are nullable, per-job,
    /// rebuildable caches sourced from the authoritative directory.
    /// </summary>
    public class GroupDestination
    {
        /// <summary>
        /// Primary key and foreign key to <c>Destinations.SyncJobId</c>.
        /// Cascade-deleted with the base destination / owning SyncJob.
        /// </summary>
        public Guid SyncJobId { get; set; }

        /// <summary>
        /// Required group object identifier. Indexed for lookup.
        /// </summary>
        public Guid GroupId { get; set; }

        /// <summary>
        /// Nullable per-job display name cache; rebuildable from directory authority.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Nullable per-job email cache; rebuildable from directory authority.
        /// </summary>
        public string Email { get; set; }
    }
}
