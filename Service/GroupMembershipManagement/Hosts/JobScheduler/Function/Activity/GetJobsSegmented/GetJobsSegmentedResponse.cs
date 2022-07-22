// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Entities;
using Microsoft.Azure.Cosmos.Table;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class GetJobsSegmentedResponse
    {
        public AsyncPageable<SyncJob> PageableQueryResult { get; set; }
        public List<SyncJob> JobsSegment;
        public string ContinuationToken;
    }
}