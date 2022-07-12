// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Services.Entities;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class BatchUpdateJobsRequest
    {
        public IEnumerable<SyncJob> SyncJobBatch;
    }
}
