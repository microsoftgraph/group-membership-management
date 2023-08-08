// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.OData.Query;
using Services.Messages.Contracts.Requests;
using SyncJobModel = Models.SyncJob;

namespace Services.Messages.Requests
{
    public class GetJobsRequest : RequestBase
    {
        public ODataQueryOptions<SyncJobModel>? QueryOptions { get; set; }
    }
}