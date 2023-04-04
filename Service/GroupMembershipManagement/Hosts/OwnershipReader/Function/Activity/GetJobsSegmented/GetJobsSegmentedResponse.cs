// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using System.Collections.Generic;

namespace Hosts.OwnershipReader
{
    public class GetJobsSegmentedResponse
    {
        public AsyncPageable<SyncJob> PageableQueryResult { get; set; }
        public List<SyncJob> JobsSegment;
        public string ContinuationToken;
    }
}