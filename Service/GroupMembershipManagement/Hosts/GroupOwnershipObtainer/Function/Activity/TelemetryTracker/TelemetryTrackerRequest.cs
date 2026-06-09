// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupOwnershipObtainer
{
    public class TelemetryTrackerRequest
    {
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncStatus JobStatus { get; set; }
        public ResultStatus ResultStatus { get; set; }
    }
}