// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.WebApi.Contracts
{
    /// <summary>
    /// Signals that feedback refinement could not be performed.
    /// Deliberately carries no message, provider detail, or inner exception so that
    /// reviewer feedback and provider responses cannot leak across the service boundary.
    /// </summary>
    public class FeedbackRefinementUnavailableException : Exception
    {
        public FeedbackRefinementUnavailableException()
            : base("Feedback refinement is unavailable.")
        {
        }
    }
}
