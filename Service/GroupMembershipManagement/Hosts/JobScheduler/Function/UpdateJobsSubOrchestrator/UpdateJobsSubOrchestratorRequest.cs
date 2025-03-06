// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class UpdateJobsSubOrchestratorRequest
    {
        public List<DistributionSyncJob> JobsToUpdate;
    }
}
