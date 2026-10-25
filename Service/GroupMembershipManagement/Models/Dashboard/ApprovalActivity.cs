// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Models.Dashboard
{
    /// <summary>
    /// Approval and rejection volume over a rolling window, surfaced on the Admin Center
    /// "Health Dashboard" tab. Automated approvals come from the AutoApprover function; manual
    /// approvals and rejections are attributed to the reviewer who made them.
    /// </summary>
    /// <remarks>
    /// Day buckets are UTC calendar days. Reviewers are derived entirely from the data
    /// (<c>ChangedByObjectId</c> / <c>ChangedByDisplayName</c>); no identities are hardcoded.
    /// </remarks>
    public class ApprovalActivity
    {
        /// <summary>Number of UTC days covered, inclusive of today.</summary>
        public int WindowDays { get; set; }

        /// <summary>Midnight UTC of the first day in the window.</summary>
        public DateTime SinceUtc { get; set; }

        /// <summary>UTC timestamp the aggregates were computed at.</summary>
        public DateTime QueriedAtUtc { get; set; }

        /// <summary>Onboarding and update requests auto-approved by the AutoApprover function.</summary>
        public int AutomatedTotal { get; set; }

        /// <summary>Submissions approved by a human reviewer.</summary>
        public int ManualTotal { get; set; }

        /// <summary>Submissions rejected by a human reviewer.</summary>
        public int RejectedTotal { get; set; }

        /// <summary>
        /// Reviewers ranked by manual volume. Reviewers beyond the top N are folded into a
        /// single row flagged with <see cref="ReviewerActivityRow.IsOther"/>.
        /// </summary>
        public List<ReviewerActivityRow> Reviewers { get; set; } = new();

        /// <summary>One entry per UTC day in the window, including days with no activity.</summary>
        public List<ApprovalActivityDay> Days { get; set; } = new();
    }

    public class ReviewerActivityRow
    {
        /// <summary>
        /// Stable series key: the reviewer's object id, or a sentinel for the unknown and
        /// aggregated-remainder rows.
        /// </summary>
        public string ReviewerKey { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
        public int Approved { get; set; }
        public int Rejected { get; set; }

        /// <summary>True when this row aggregates every reviewer outside the top N.</summary>
        public bool IsOther { get; set; }
    }

    public class ApprovalActivityDay
    {
        /// <summary>Midnight UTC of the day this bucket covers.</summary>
        public DateTime Date { get; set; }

        public int Automated { get; set; }

        /// <summary>Per-reviewer counts for this day, aligned to <see cref="ApprovalActivity.Reviewers"/>.</summary>
        public List<ReviewerDayCount> Reviewers { get; set; } = new();
    }

    public class ReviewerDayCount
    {
        public string ReviewerKey { get; set; } = string.Empty;
        public int Approved { get; set; }
        public int Rejected { get; set; }
    }
}
