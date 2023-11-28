// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class PostJobResponse : ResponseBase
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? ErrorCode { get; set; }
        public List<string>? ResponseData { get; set; }
        public Guid? NewSyncJobId { get; set; }
    }
}
