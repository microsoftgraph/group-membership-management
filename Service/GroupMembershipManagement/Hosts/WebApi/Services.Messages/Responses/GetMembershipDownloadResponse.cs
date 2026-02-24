// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetMembershipDownloadResponse : ResponseBase
    {
        public byte[]? FileContent { get; set; }
        public string? FileName { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}
