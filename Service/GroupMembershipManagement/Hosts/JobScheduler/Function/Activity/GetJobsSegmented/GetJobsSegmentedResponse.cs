// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Entities;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class GetJobsSegmentedResponse
    {
        public AsyncPageable<SyncJob> PageableQueryResult { get; set; }
        public List<DistributionSyncJob> JobsSegment;
        public string ContinuationToken;
    }
}