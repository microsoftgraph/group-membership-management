// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace WebApi.Models.DTOs
{
    /// <summary>
    /// Result of spot-checking whether a single user is included by each source part of a sync job.
    /// Only GroupMembership and SqlMembership source parts are evaluated; all other types are reported as unsupported.
    /// </summary>
    public class SpotCheckResult
    {
        /// <summary>
        /// Whether the checked user's Entra ID account is currently enabled.
        /// When false, source parts are not evaluated.
        /// </summary>
        public bool AccountEnabled { get; set; }

        /// <summary>
        /// True when at least one source part is of a type that cannot be evaluated (e.g. GroupOwnership, PlaceMembership, TeamsChannelMembership).
        /// </summary>
        public bool HasUnsupportedParts { get; set; }

        public List<SpotCheckPartResult> Parts { get; set; } = new List<SpotCheckPartResult>();
    }

    public class SpotCheckPartResult
    {
        /// <summary>
        /// Zero-based index of the source part within the job's query array. Used by the UI to correlate to the displayed source part.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// The source part type as stored in the query (e.g. "GroupMembership", "SqlMembership").
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Whether this source part type is supported by the spot-check.
        /// </summary>
        public bool Supported { get; set; }

        /// <summary>
        /// Whether this source part is exclusionary.
        /// </summary>
        public bool Exclusionary { get; set; }

        /// <summary>
        /// Whether the user is included by this source part's source set.
        /// Null when the result could not be determined (unsupported type, or supporting data unavailable).
        /// </summary>
        public bool? Included { get; set; }
    }
}
