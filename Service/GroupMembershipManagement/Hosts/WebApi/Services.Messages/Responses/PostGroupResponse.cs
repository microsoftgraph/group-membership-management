// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class PostGroupResponse : ResponseBase
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? ErrorCode { get; set; }
        public List<string>? ResponseData { get; set; }
        public Guid? GroupId { get; set; }
    }
}
