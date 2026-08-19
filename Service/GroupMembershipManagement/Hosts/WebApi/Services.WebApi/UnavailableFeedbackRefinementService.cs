// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi.Contracts;

namespace Services.WebApi
{
    /// <summary>
    /// Fallback registered when the approved AI endpoint is not configured. It produces the
    /// same content-free unavailable signal so the endpoint returns a deterministic 503
    /// instead of failing controller activation.
    /// </summary>
    public class UnavailableFeedbackRefinementService : IFeedbackRefinementService
    {
        public Task<string> RefineAsync(string feedback)
        {
            throw new FeedbackRefinementUnavailableException();
        }
    }
}
