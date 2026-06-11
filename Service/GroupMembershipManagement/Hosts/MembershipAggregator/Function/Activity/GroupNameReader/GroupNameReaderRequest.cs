// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class GroupNameReaderRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required Guid GroupId { get; init; }
    }
}