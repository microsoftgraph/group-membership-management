// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class GetJobsResponse
    {
        public List<DistributionSyncJob> JobsSegment { get; set; }
    }
}