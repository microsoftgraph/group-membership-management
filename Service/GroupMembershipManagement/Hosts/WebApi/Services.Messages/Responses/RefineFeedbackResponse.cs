// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class RefineFeedbackResponse : ResponseBase
    {
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// The refined feedback. Populated only when <see cref="StatusCode"/> is 200.
        /// </summary>
        public string? RefinedText { get; set; }

        /// <summary>
        /// Stable, content-free error code. Null when <see cref="StatusCode"/> is 200.
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// Safe message that never contains feedback, model output, provider detail, or exception detail.
        /// </summary>
        public string ResponseMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Stable error codes returned by the feedback refinement endpoint.
    /// </summary>
    public static class RefineFeedbackErrorCodes
    {
        public const string InvalidRequest = "InvalidRequest";
        public const string FeedbackTooLong = "FeedbackTooLong";
        public const string FeatureDisabled = "FeatureDisabled";
        public const string InvalidRefinedText = "InvalidRefinedText";
        public const string RefinedTextTooLong = "RefinedTextTooLong";
        public const string Timeout = "Timeout";
        public const string ServiceUnavailable = "ServiceUnavailable";
        public const string InternalError = "InternalError";
    }
}
