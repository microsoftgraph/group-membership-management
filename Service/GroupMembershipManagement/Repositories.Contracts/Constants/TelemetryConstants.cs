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

        // Dimension names for the SyncComplete custom metric.
        public const string ResultDimensionName = "Result";
        public const string TypeDimensionName = "Type";

        // Dimension names for ResourceUnitsMetricName. These contribute to the
        // App Insights metric identity, so all GetMetric call sites must use
        // the same names in the same order to avoid identity drift.
        public const string OperationTypeDimensionName = "OperationType";
        public const string QueryTypeDimensionName = "QueryType";
    }
}
