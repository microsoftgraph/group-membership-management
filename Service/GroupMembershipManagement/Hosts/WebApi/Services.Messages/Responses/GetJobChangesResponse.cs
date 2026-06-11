// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.SyncJobChange;
using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetJobChangesResponse : ResponseBase
    {
        public RepositoryPage<SyncJobChange>? Changes { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}
