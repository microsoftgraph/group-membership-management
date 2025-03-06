// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class BatchUpdateJobsRequest
    {
        public IEnumerable<DistributionSyncJob> SyncJobBatch;
    }
}
