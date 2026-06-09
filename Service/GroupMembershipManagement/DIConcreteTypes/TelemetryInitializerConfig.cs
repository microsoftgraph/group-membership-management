// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace DIConcreteTypes
{
    /// <summary>
    /// Controls category filtering behavior for <see cref="TelemetryInitializer" />.
    /// </summary>
    public class TelemetryInitializerConfig
    {
        /// <summary>
        /// Gets the built-in category prefixes that are eligible for GMM tagging.
        /// </summary>
        public static IReadOnlyList<string> DefaultAllowedPrefixes { get; } = Array.AsReadOnly(new[]
        {
            "Services.",
            "Repositories.",
            "Hosts.",
            "SqlMembershipObtainer.",
            "JobTrigger.",
            "JobScheduler.",
            "MembershipAggregator.",
            "GroupMembershipObtainer.",
            "GroupOwnershipObtainer.",
            "SyncJobUpdater.",
            "DestinationAttributesUpdater.",
            "MessageSplitter.",
            "AzureUserReader.",
            "GraphUpdater.",
            "Notifier.",
            "AzureMaintenance.",
            "TeamsChannelMembershipObtainer.",
            "TeamsChannelUpdater.",
            "NonProdService.",
            "PlaceMembershipObtainer."
        });

        /// <summary>
        /// Gets or sets extra allowed prefixes that are merged with the built-in defaults.
        /// </summary>
        public List<string> AdditionalAllowedPrefixes { get; set; } = [];
    }
}

