// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.TeamsChannelUpdater
{
    public class TelemetryTrackerRequest
    {
        public SyncStatus JobStatus { get; set; }
        public ResultStatus ResultStatus { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}