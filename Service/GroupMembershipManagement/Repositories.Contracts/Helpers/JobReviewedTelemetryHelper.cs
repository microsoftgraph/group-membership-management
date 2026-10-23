// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Models.ServiceBus;
using Models.SyncJobChange;
using System;

namespace Repositories.Contracts.Helpers
{
    /// <summary>
    /// What kind of submission a review decided. A submission is either a brand new job
    /// (<see cref="Onboarding"/>) or an edit to a job that already exists (<see cref="Update"/>).
    /// Carried as its own dimension rather than folded into the reviewal type so that the two stay
    /// independent: every combination of who decided and what they decided on is a real case.
    /// </summary>
    public enum JobReviewRequestType
    {
        Onboarding,
        Update
    }

    /// <summary>
    /// The verdict a review reached. Only decisions that took effect are recorded, so a submission
    /// that the auto-approver merely declined to approve is not a <see cref="Rejected"/> review: it
    /// is routed to a human and emits its own record when that human decides.
    /// </summary>
    public enum JobReviewOutcome
    {
        Approved,
        Rejected
    }

    /// <summary>
    /// Single source of truth for JobReviewed telemetry, emitted once per onboarding or update
    /// submission whose review has been durably persisted. The emitters live in different hosts and
    /// the dashboard queries them by name, so the metric name and the dimension names are centralised
    /// here rather than repeated as string literals at each call site.
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

        /// <summary>
        /// Reviewer identifier recorded when a policy precheck rejects a submission regardless of what
        /// the reviewer chose - today, a submitter who is no longer an owner of the destination. Kept
        /// distinct from <see cref="AutoReviewerId"/> because the AutoApprover host did not make this
        /// call, and kept out of the reviewer's own identity because the reviewer did not choose it.
        /// </summary>
        public const string SystemReviewerId = "System";

        private const string AutoReviewalType = "Auto";
        private const string ManualReviewalType = "Manual";

        /// <summary>
        /// Maps the audit reason of the submission under review onto the request type. Centralised so
        /// the reason-to-type mapping cannot drift between the approval and rejection call sites.
        /// Anything that is not an <see cref="SyncJobChangeReason.Update"/> is an onboarding, which
        /// matches the submission reasons a reviewer can act on (Onboarding and Update) and keeps a
        /// missing or unexpected reason out of the update figures.
        /// </summary>
        public static JobReviewRequestType ToRequestType(string? submissionChangeReason)
        {
            return string.Equals(submissionChangeReason, SyncJobChangeReason.Update.ToString(), StringComparison.OrdinalIgnoreCase)
                ? JobReviewRequestType.Update
                : JobReviewRequestType.Onboarding;
        }

        /// <summary>
        /// Maps the origin of an auto-approval queue message onto the request type. The origin already
        /// defaults to onboarding for messages serialized before it existed, so this needs no fallback.
        /// </summary>
        public static JobReviewRequestType ToRequestType(AutoApprovalOrigin origin)
        {
            return origin is AutoApprovalOrigin.Update or AutoApprovalOrigin.ConfigUpdate
                ? JobReviewRequestType.Update
                : JobReviewRequestType.Onboarding;
        }

        public static void TrackAutoApproval(TelemetryClient telemetryClient, Guid syncJobId, Guid targetOfficeGroupId, string membershipType, JobReviewRequestType requestType)
        {
            Track(telemetryClient, syncJobId, targetOfficeGroupId, membershipType, AutoReviewalType, AutoReviewerId, requestType, JobReviewOutcome.Approved);
        }

        public static void TrackManualApproval(TelemetryClient telemetryClient, Guid syncJobId, Guid targetOfficeGroupId, string membershipType, JobReviewRequestType requestType, string reviewerId)
        {
            Track(telemetryClient, syncJobId, targetOfficeGroupId, membershipType, ManualReviewalType, reviewerId, requestType, JobReviewOutcome.Approved);
        }

        public static void TrackManualRejection(TelemetryClient telemetryClient, Guid syncJobId, Guid targetOfficeGroupId, string membershipType, JobReviewRequestType requestType, string reviewerId)
        {
            Track(telemetryClient, syncJobId, targetOfficeGroupId, membershipType, ManualReviewalType, reviewerId, requestType, JobReviewOutcome.Rejected);
        }

        /// <summary>
        /// Records a rejection a policy precheck forced, not one a reviewer chose. Attributed to
        /// <see cref="SystemReviewerId"/> so it never lands in a named reviewer's manual breakdown.
        /// </summary>
        public static void TrackSystemRejection(TelemetryClient telemetryClient, Guid syncJobId, Guid targetOfficeGroupId, string membershipType, JobReviewRequestType requestType)
        {
            Track(telemetryClient, syncJobId, targetOfficeGroupId, membershipType, AutoReviewalType, SystemReviewerId, requestType, JobReviewOutcome.Rejected);
        }

        // Recorded as MetricTelemetry rather than as a custom event because custom events
        // are subject to ingestion sampling. Reviews are rare, so sampling does not
        // average out across them the way it does for high-volume telemetry: a dropped
        // event removes a whole review from the count, and the per-reviewer breakdown
        // would attribute the loss to one named reviewer. Metric telemetry is not sampled.
        //
        // Sent as a direct MetricTelemetry rather than through TelemetryClient.GetMetric(),
        // for the same reason as SyncCompleteTelemetryHelper: GetMetric pre-aggregates
        // in-process for ~60s before flushing, and discards the aggregate if the host
        // recycles first. Sum and Count are 1 because each call records one review, which
        // is what lets the dashboard sum valueSum to get an exact count and still drill
        // into individual reviews through the dimensions below.
        private static void Track(
            TelemetryClient telemetryClient,
            Guid syncJobId,
            Guid targetOfficeGroupId,
            string membershipType,
            string reviewalType,
            string reviewerId,
            JobReviewRequestType requestType,
            JobReviewOutcome outcome)
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
            metric.Properties["RequestType"] = requestType.ToString();
            metric.Properties["Outcome"] = outcome.ToString();

            telemetryClient.TrackMetric(metric);
        }
    }
}
