// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.WebApi.Contracts
{
    public interface IFeedbackRefinementService
    {
        /// <summary>
        /// Rewrites reviewer-authored rejection feedback for clarity and professionalism
        /// while preserving meaning, and returns only the rewritten text.
        /// </summary>
        /// <exception cref="TimeoutException">The approved AI service did not respond in time.</exception>
        /// <exception cref="FeedbackRefinementUnavailableException">The approved AI service is not configured or not available.</exception>
        Task<string> RefineAsync(string feedback);
    }
}
