// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class TelemetryTrackerRequest
    {
        public SyncStatus JobStatus { get; set; }
        public ResultStatus ResultStatus { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}