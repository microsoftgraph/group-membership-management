// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Entities;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class ResetJobsRequest
    {
        public List<DistributionSyncJob> JobsToReset;
    }
}
