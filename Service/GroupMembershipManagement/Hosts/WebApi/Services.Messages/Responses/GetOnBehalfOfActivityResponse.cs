// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Dashboard;
using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetOnBehalfOfActivityResponse : ResponseBase
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? ErrorCode { get; set; }
        public OnBehalfOfActivity Activity { get; set; } = new();
    }
}
