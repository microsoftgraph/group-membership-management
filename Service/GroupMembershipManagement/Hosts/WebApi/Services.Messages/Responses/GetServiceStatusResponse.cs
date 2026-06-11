// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetServiceStatusResponse : ResponseBase
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? ErrorCode { get; set; }
        public ServiceStatuses Status { get; set; }
    }
}
