// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Models.AdfRun
{
    /// <summary>
    /// Represents an Azure Data Factory pipeline run record
    /// </summary>
    public class AdfRun
    {
        /// <summary>
        /// Gets or sets the unique identifier for the ADF run record
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the ADF pipeline run identifier
        /// </summary>
        public string AdfRunId { get; set; }

        /// <summary>
        /// Gets or sets notes or description about this pipeline run (e.g., to communicate issues with faulty runs)
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Notes for this run are active and should be surfaced to users.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Gets or sets the identifier of the user who created this record (e.g., who authored the Notes).
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this record was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the Notes field was last modified
        /// </summary>
        public DateTime? NotesModifiedAt { get; set; }
    }
}
