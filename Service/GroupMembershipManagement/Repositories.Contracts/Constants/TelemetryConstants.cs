// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.Constants
{
    public static class TelemetryConstants
    {
        public const string ResourceUnitsMetricName = "ResourceUnitsUsed";
        public const string ThrottleLimitMetricName = "ThrottleLimitPercentage";
        public const string ResourceUnitsEventName = "ResourceUnitsUsedByType";
        public const string SyncCompleteName = "SyncComplete";
        public const string SyncJobTimeElapsedSecondsMetricName = "SyncJobTimeElapsedSeconds";
        public const string NumberOfJobsStartedName = "NumberOfJobsStarted";
        public const string NumberOfJobsCompletedName = "NumberOfJobsCompleted";
        public const string TelemetrySchemaVersionPropertyName = "TelemetrySchemaVersion";
        public const string RichMetricSchemaVersion = "2";

        // Dimension names for the SyncComplete custom metric.
        public const string ResultDimensionName = "Result";
        public const string TypeDimensionName = "Type";

        // Dimension names for ResourceUnitsMetricName. These contribute to the
        // App Insights metric identity, so all GetMetric call sites must use
        // the same names in the same order to avoid identity drift.
        public const string OperationTypeDimensionName = "OperationType";
        public const string QueryTypeDimensionName = "QueryType";

        // Discriminator that segments channel-path RUU from group-path RUU on the
        // ResourceUnitsUsed metric and ResourceUnitsUsedByType event (FR-RUU-001).
        public const string MembershipTypeDimensionName = "MembershipType";

        // Distinguishes resource units read from the x-ms-resource-unit response header from
        // estimates (for example the fixed delta-query cost) on the group-membership
        // path only: an opt-in ResourceUnitsUsedByType event property and ResourceUnitsBySourceMetricName (FR-020).
        public const string ResourceUnitSourceDimensionName = "ResourceUnitSource";
        public const string ResourceUnitSourceHeader = "Header";
        public const string ResourceUnitSourceEstimated = "Estimated";

        // Group-membership-only metric that carries the source dimension, so the shared
        // ResourceUnitsUsed metric keeps its existing identity for every other caller.
        public const string ResourceUnitsBySourceMetricName = "GroupMembershipResourceUnitsBySource";
    }
}
