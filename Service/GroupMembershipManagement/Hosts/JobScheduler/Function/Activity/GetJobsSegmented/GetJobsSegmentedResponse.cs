// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class GetJobsSegmentedResponse
    {
        public string Query { get; set; }
        public string ContinuationToken { get; set; }
        public List<DistributionSyncJob> JobsSegment { get; set; }
    }
}