// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi.Contracts;

namespace Services.WebApi
{
    /// <summary>
    /// A single edit operation emitted by the model (under a strict json_schema) and applied
    /// server-side to the inbound workingQuery. The model only proposes; the server is authoritative.
    /// </summary>
    public class EditOperation
    {
        /// <summary>One of "set", "add", "remove", "replace" (case-insensitive).</summary>
        public string Op { get; set; } = string.Empty;

        /// <summary>
        /// Required for "remove" and "replace"; MUST match an inbound part (safety gate).
        /// Omitted for "add" (the server mints it). Ignored for "set".
        /// </summary>
        public string? PartId { get; set; }

        /// <summary>Payload for "add" and "replace". The model MUST NOT set PartId on an added part.</summary>
        public CopilotSourcePartResult? Part { get; set; }

        /// <summary>For "set": the complete replacement set of SUPPORTED parts only.</summary>
        public List<CopilotSourcePartResult>? Parts { get; set; }
    }

    /// <summary>
    /// The model-emitted structured response: a natural-language message plus an ordered operation set.
    /// </summary>
    public class CopilotOperationResponse
    {
        public string Message { get; set; } = string.Empty;
        public List<EditOperation> Operations { get; set; } = new();
    }

    /// <summary>
    /// Result of applying an operation set server-side to a working query.
    /// </summary>
    public class CopilotOperationApplyResult
    {
        /// <summary>The complete resulting query after all operations were applied.</summary>
        public List<CopilotSourcePartResult> ResultingQuery { get; set; } = new();

        /// <summary>Machine-readable summary of the operations applied (op + partId).</summary>
        public List<CopilotOperationSummary> AppliedOperations { get; set; } = new();

        /// <summary>Set when the resulting query is empty. Soft warning, not a block.</summary>
        public string? Warning { get; set; }

        /// <summary>Set (e.g., "UnknownPartTarget") when the whole set is rejected atomically.</summary>
        public string? ErrorCode { get; set; }

        // Structured counts for observability (no raw filters/PII).
        public int AddCount { get; set; }
        public int RemoveCount { get; set; }
        public int ReplaceCount { get; set; }
        public int RejectedTargetCount { get; set; }
        public int PreservedUnsupportedCount { get; set; }
    }
}
