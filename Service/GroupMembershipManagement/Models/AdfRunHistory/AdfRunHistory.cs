// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
namespace Models.AdfRunHistory
{
    /// <summary>
    /// Identifies an ADF run whose published SQL table is eligible for consumers.
    /// </summary>
    public sealed class AdfRunHistory
    {
        public const string NoEligibleRunMessage = "No eligible ADF run is available; see the AdfRunHistoryRepository warning for the cause.";

        /// <summary>
        /// Gets or sets the actual Azure Data Factory pipeline run identifier.
        /// </summary>
        public Guid AdfRunId { get; set; }

        /// <summary>
        /// Gets or sets the UTC time at which the data set became eligible.
        /// </summary>
        public DateTime PublishedAtUtc { get; set; }

        public string GetTableName() => AdfRunId.ToString().Replace("-", string.Empty);
    }
}
