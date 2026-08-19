// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models
{
    /// <summary>
    /// Server-side safety limits for AI rejection-feedback refinement.
    /// </summary>
    public static class FeedbackRefinementLimits
    {
        /// <summary>
        /// Maximum length of refined output that may be returned to the reviewer.
        /// This is an output-only ceiling. It must not be applied to reviewer-entered
        /// feedback or to the existing rejection submission path.
        /// </summary>
        public const int MaxRefinedTextLength = 4000;

        /// <summary>
        /// Maximum length of reviewer feedback that may be sent to the approved AI service.
        /// This bounds the cost and latency of a single refinement request, which is retried
        /// with backoff by the provider client. It applies only to the refinement endpoint and
        /// must not be applied to the existing rejection submission path, so a reviewer whose
        /// feedback exceeds it can still reject normally, just without refinement.
        /// </summary>
        public const int MaxInputLength = 8000;
    }
}
