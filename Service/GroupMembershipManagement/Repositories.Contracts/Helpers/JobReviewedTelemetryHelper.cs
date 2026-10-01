// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using System;

namespace Repositories.Contracts.Helpers
{
    /// <summary>
    /// Single source of truth for JobReviewed telemetry, emitted once per onboarding
    /// approval that has been durably persisted. The two emitters live in different
    /// hosts and the dashboard queries them by name, so the metric name and the
    /// dimension names are centralised here rather than repeated as string literals
    /// at each call site.
    /// </summary>
    public static class JobReviewedTelemetryHelper
    {
        public const string MetricName = "JobReviewed";

        /// <summary>
        /// Reviewer identifier recorded for every automatic approval. Deliberately not a GUID, so it
        /// can never collide with a reviewer's object id in the per-reviewer dashboard panel, and
        /// never a person.
        /// </summary>
        public const string AutoReviewerId = "AutoApprover";

        private const string AutoReviewalType = "Auto";
        private const string ManualReviewalType = "Manual";

        public static void TrackAutoReview(TelemetryClient telemetryClient, Guid syncJobId, Guid targetOfficeGroupId, string membershipType)
        {
            Track(telemetryClient, syncJobId, targetOfficeGroupId, membershipType, AutoReviewalType, AutoReviewerId);
        }

        public static void TrackManualReview(TelemetryClient telemetryClient, Guid syncJobId, Guid targetOfficeGroupId, string membershipType, string reviewerId)
        {
            Track(telemetryClient, syncJobId, targetOfficeGroupId, membershipType, ManualReviewalType, reviewerId);
        }

        // Recorded as MetricTelemetry rather than as a custom event because custom events
        // are subject to ingestion sampling. Approvals are rare, so sampling does not
        // average out across them the way it does for high-volume telemetry: a dropped
        // event removes a whole approval from the count, and the per-reviewer breakdown
        // would attribute the loss to one named reviewer. Metric telemetry is not sampled.
        //
        // Sent as a direct MetricTelemetry rather than through TelemetryClient.GetMetric(),
        // for the same reason as SyncCompleteTelemetryHelper: GetMetric pre-aggregates
        // in-process for ~60s before flushing, and discards the aggregate if the host
        // recycles first. Sum and Count are 1 because each call records one approval, which
        // is what lets the dashboard sum valueSum to get an exact count and still drill
        // into individual approvals through the dimensions below.
        private static void Track(TelemetryClient telemetryClient, Guid syncJobId, Guid targetOfficeGroupId, string membershipType, string reviewalType, string reviewerId)
        {
            if (telemetryClient is null)
            {
                return;
            }

            var metric = new MetricTelemetry
            {
                Name = MetricName,
                Sum = 1,
                Count = 1
            };

            metric.Properties["SyncJobId"] = syncJobId.ToString();
            metric.Properties["TargetOfficeGroupId"] = targetOfficeGroupId.ToString();
            metric.Properties["MembershipType"] = membershipType ?? string.Empty;
            metric.Properties["ReviewalType"] = reviewalType;
            metric.Properties["ReviewerId"] = reviewerId ?? string.Empty;

            telemetryClient.TrackMetric(metric);
        }
    }
}
