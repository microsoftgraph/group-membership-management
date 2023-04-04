// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;

namespace Hosts.OwnershipReader
{
    public class GetJobsSegmentedRequest
    {
        public AsyncPageable<SyncJob> PageableQueryResult;
        public string ContinuationToken;
    }
}
