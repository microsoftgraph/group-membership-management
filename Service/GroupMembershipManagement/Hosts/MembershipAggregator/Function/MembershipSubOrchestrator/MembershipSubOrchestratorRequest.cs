// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.DurableTask.Entities;
using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required EntityInstanceId EntityId { get; init; }
        public required Guid GroupId { get; init; }
    }
}
