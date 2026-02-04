// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.SyncJobHistory;
using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetSyncJobHistoryResponse : ResponseBase
    {
        public List<SyncJobHistory>? History { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}
