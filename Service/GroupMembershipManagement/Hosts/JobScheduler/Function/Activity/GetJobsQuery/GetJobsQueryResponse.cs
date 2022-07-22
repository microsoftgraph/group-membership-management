// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.Cosmos.Table;
using System.Collections.Generic;
using System.Linq;

namespace Hosts.JobScheduler
{
    public class GetJobsQueryResponse
    {
        public IQueryable<SyncJob> JobsQuery;
    }
}
