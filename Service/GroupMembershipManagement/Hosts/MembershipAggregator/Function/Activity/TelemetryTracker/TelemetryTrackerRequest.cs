// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.MembershipAggregator
{
    public class TelemetryTrackerRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required SyncStatus JobStatus { get; init; }
        public required ResultStatus ResultStatus { get; init; }
    }
}