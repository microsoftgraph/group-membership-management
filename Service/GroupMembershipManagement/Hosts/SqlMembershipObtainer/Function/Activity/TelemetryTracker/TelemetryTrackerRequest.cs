// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace SqlMembershipObtainer
{
    public class TelemetryTrackerRequest
    {
        public required SyncStatus JobStatus { get; init; }
        public required ResultStatus ResultStatus { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
    }
}