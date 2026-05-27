// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetSyncExplanationResponse : ResponseBase
    {
        public string Explanation { get; set; } = string.Empty;
        public HttpStatusCode StatusCode { get; set; }
    }
}
