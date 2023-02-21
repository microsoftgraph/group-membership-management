// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using System;

namespace Hosts.GraphUpdater
{
    public class TelemetryTrackerRequest
    {
        public SyncStatus JobStatus { get; set; }
        public ResultStatus ResultStatus { get; set; }
        public Guid? RunId { get; set; }
    }
}