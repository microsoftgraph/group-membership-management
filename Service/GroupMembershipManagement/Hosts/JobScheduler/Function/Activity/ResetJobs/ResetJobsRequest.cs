// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class ResetJobsRequest
    {
        public List<DistributionSyncJob> JobsToReset;
    }
}
