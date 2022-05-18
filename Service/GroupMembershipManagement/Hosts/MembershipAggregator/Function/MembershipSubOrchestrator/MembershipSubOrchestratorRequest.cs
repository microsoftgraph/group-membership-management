// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorRequest
    {
        public SyncJob SyncJob { get; set; }
        public EntityId EntityId { get; set; }
    }
}
