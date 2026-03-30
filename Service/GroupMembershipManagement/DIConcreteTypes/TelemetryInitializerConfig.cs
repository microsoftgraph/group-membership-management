// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
        public static IReadOnlyList<string> DefaultAllowedPrefixes { get; } =
        [
            "Services.",
            "Repositories.",
            "Hosts.",
            "SqlMembershipObtainer.",
            "JobTrigger.",
            "MembershipAggregator.",
            "GroupMembershipObtainer.",
            "SyncJobUpdater.",
            "DestinationAttributesUpdater.",
            "MessageSplitter."
        ];

        /// <summary>
        /// Gets or sets extra allowed prefixes that are merged with the built-in defaults.
        /// </summary>
        public List<string> AdditionalAllowedPrefixes { get; set; } = [];
    }
}
