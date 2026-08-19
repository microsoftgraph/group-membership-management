// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class RefineFeedbackRequest : RequestBase
    {
        public RefineFeedbackRequest(string feedback)
        {
            Feedback = feedback;
        }

        /// <summary>
        /// The reviewer-authored rejection feedback currently shown in the editor.
        /// Bounded by <c>FeedbackRefinementLimits.MaxInputLength</c> for refinement only;
        /// no restriction is applied to the rejection the reviewer ultimately submits.
        /// </summary>
        public string Feedback { get; private set; }
    }
}
