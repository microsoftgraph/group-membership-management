// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models
{
    /// <summary>
    /// Base row of the consolidated Table-Per-Type (TPT) destination model.
    /// One row exists per supported <see cref="SyncJob"/>. Concrete
    /// <see cref="GroupDestination"/> and <see cref="TeamsChannelDestination"/>
    /// entities share this key and persist their details in separate tables.
    /// </summary>
    public abstract class Destination
    {
        private readonly string _expectedDestinationType;
        private string _destinationType;

        protected Destination(MembershipTypes destinationType)
        {
            _expectedDestinationType = destinationType.ToString();
            DestinationType = _expectedDestinationType;
        }

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
        /// Set by the concrete constructor, not independently mutable. EF validates the
        /// stored value on materialization; it is not an EF TPH discriminator.
        /// </summary>
        public string DestinationType
        {
            get => _destinationType;
            private set
            {
                if (!string.Equals(value, _expectedDestinationType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The stored destination type does not match its CLR subtype.");
                }

                _destinationType = value;
            }
        }
    }
}
