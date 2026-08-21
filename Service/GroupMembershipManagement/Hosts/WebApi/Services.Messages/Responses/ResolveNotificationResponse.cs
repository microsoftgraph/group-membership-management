// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Net;
using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class ResolveNotificationResponse : ResponseBase
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    }
}
