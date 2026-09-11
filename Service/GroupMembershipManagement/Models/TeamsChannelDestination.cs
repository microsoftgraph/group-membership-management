// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models
{
    /// <summary>
    /// Per-type destination for a Teams-channel SyncJob. Shares its primary key
    /// (<see cref="SyncJobId"/>) with the base <see cref="Destination"/> row
    /// (Table-Per-Type shared key). Retains both the team object identity and the
    /// channel identity; it must never be reduced to group-only semantics. TeamName and
    /// ChannelName are nullable, per-job, rebuildable caches. Teams channels have no email.
    /// </summary>
    public class TeamsChannelDestination
    {
        /// <summary>
        /// Primary key and foreign key to <c>Destinations.SyncJobId</c>.
        /// Cascade-deleted with the base destination / owning SyncJob.
        /// </summary>
        public Guid SyncJobId { get; set; }

        /// <summary>
        /// Required team object identifier. Indexed for lookup.
        /// </summary>
        public Guid TeamId { get; set; }

        /// <summary>
        /// Required, non-empty channel identifier. Preserves the existing legacy
        /// storage capacity (nvarchar(255)) without normalization changes.
        /// </summary>
        public string ChannelId { get; set; }

        /// <summary>
        /// Nullable per-job team display-name cache; rebuildable from directory authority.
        /// </summary>
        public string TeamName { get; set; }

        /// <summary>
        /// Nullable per-job channel display-name cache; rebuildable from directory authority.
        /// </summary>
        public string ChannelName { get; set; }
    }
}
