// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required EntityId EntityId { get; init; }
        public required Guid GroupId { get; init; }
    }
}
