// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Models.Dashboard
{
    /// <summary>
    /// Onboarding and Update requests that a submitter filed on behalf of a different group
    /// owner, over a rolling window. Surfaced on the Admin Center "Dashboard" tab.
    /// </summary>
    /// <remarks>
    /// A request counts as "on behalf of" only when <c>ChangedOnBehalfOfObjectId</c> is set, which
    /// happens when the submitter picked an owner other than themselves. The view is
    /// submitter-centric: series are keyed by <c>ChangedByObjectId</c> (the actor who filed it).
    /// Day buckets are UTC calendar days and no identities are hardcoded.
    /// </remarks>
    public class OnBehalfOfActivity
    {
        /// <summary>Number of UTC days covered, inclusive of today.</summary>
        public int WindowDays { get; set; }

        /// <summary>Midnight UTC of the first day in the window.</summary>
        public DateTime SinceUtc { get; set; }

        /// <summary>UTC timestamp the aggregates were computed at.</summary>
        public DateTime QueriedAtUtc { get; set; }

        /// <summary>
        /// On-behalf-of Onboarding requests across every submitter, including those folded into
        /// the aggregated remainder row, so this always reconciles with <see cref="Submitters"/>.
        /// </summary>
        public int OnboardingTotal { get; set; }

        /// <summary>
        /// On-behalf-of Update requests across every submitter, including those folded into the
        /// aggregated remainder row.
        /// </summary>
        public int UpdateTotal { get; set; }

        /// <summary>
        /// Every on-behalf-of request in the window. Both operands span all submitters, so this
        /// reconciles with the per-submitter rows instead of double counting the remainder.
        /// </summary>
        public int GrandTotal => OnboardingTotal + UpdateTotal;

        /// <summary>
        /// Submitters ranked by request volume. Submitters beyond the top N are folded into a
        /// single row flagged with <see cref="SubmitterActivityRow.IsOther"/>.
        /// </summary>
        public List<SubmitterActivityRow> Submitters { get; set; } = new();

        /// <summary>One entry per UTC day in the window, including days with no activity.</summary>
        public List<OnBehalfOfActivityDay> Days { get; set; } = new();
    }

    public class SubmitterActivityRow
    {
        /// <summary>
        /// Stable series key: the submitter's object id, or a sentinel for the unknown and
        /// aggregated-remainder rows.
        /// </summary>
        public string SubmitterKey { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
        public int Onboarding { get; set; }
        public int Update { get; set; }

        /// <summary>True when this row aggregates every submitter outside the top N.</summary>
        public bool IsOther { get; set; }
    }

    public class OnBehalfOfActivityDay
    {
        /// <summary>Midnight UTC of the day this bucket covers.</summary>
        public DateTime Date { get; set; }

        /// <summary>Per-submitter counts, aligned to <see cref="OnBehalfOfActivity.Submitters"/>.</summary>
        public List<SubmitterDayCount> Submitters { get; set; } = new();
    }

    public class SubmitterDayCount
    {
        public string SubmitterKey { get; set; } = string.Empty;
        public int Onboarding { get; set; }
        public int Update { get; set; }
    }
}
