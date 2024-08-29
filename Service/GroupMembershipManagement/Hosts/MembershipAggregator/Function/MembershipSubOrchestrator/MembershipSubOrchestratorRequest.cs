// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.DurableTask.Entities;
using Models;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorRequest
    {
        public SyncJob SyncJob { get; set; }
        public EntityInstanceId EntityId { get; set; }
    }
}
