// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Models.Dashboard
{
    /// <summary>
    /// Aggregate health counters for the sync job table, surfaced on the Admin Center
    /// "Dashboard" tab. Every section is a GROUP BY over [dbo].[SyncJobs]; no
    /// individual job rows are returned.
    /// </summary>
    public class DashboardSummary
    {
        /// <summary>Jobs awaiting review, split into Onboarding (never run) and Update.</summary>
        public List<PendingReviewSummaryRow> PendingReview { get; set; } = new();

        /// <summary>Jobs currently mid-run, including the stuck/errored-stuck variants.</summary>
        public List<InProgressStatusRow> InProgress { get; set; } = new();

        /// <summary>Jobs sitting in a status that is neither healthy nor an expected pause.</summary>
        public List<AnomalousStatusRow> AnomalousStatuses { get; set; } = new();

        /// <summary>UTC timestamp the aggregates were computed at.</summary>
        public DateTime QueriedAtUtc { get; set; }
    }

    public class PendingReviewSummaryRow
    {
        /// <summary>Either "Onboarding" or "Update".</summary>
        public string RequestType { get; set; } = string.Empty;
        public int Count { get; set; }

        /// <summary>Change time of the longest-waiting request in this bucket.</summary>
        public DateTime? OldestChangeTime { get; set; }
    }

    public class InProgressStatusRow
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }

        /// <summary>
        /// Start time of the longest-running job in this bucket, or null when every job in
        /// the bucket still carries the never-run sentinel.
        /// </summary>
        public DateTime? OldestStartTime { get; set; }
    }

    public class AnomalousStatusRow
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }

        /// <summary>Highest number of sync cycles missed by any job in this bucket.</summary>
        public int? MaxCyclesMissed { get; set; }

        /// <summary>Oldest successful run among jobs that have succeeded at least once.</summary>
        public DateTime? OldestLastSuccessfulRun { get; set; }

        /// <summary>Jobs in this bucket that have never had a successful run.</summary>
        public int NeverSucceededCount { get; set; }
    }
}
