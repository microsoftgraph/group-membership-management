// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class TelemetryTrackerRequest
    {
        public required SyncStatus JobStatus { get; init; }
        public required ResultStatus ResultStatus { get; init; }
        public required Guid RunId { get; init; }
    }
}