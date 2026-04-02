// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetThresholdNotificationResponse : ResponseBase
    {
        public int ChangeQuantityForAdditions { get; set; }
        public double ChangePercentageForAdditions { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ChangeQuantityForRemovals { get; set; }
        public double ChangePercentageForRemovals { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}
