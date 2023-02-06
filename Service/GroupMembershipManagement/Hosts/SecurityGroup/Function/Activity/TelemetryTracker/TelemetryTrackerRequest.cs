// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using System;

namespace Hosts.SecurityGroup
{
    public class TelemetryTrackerRequest
    {
        public SyncStatus JobStatus { get; set; }
        public ResultStatus ResultStatus { get; set; }
        public Guid? RunId { get; set; }
    }
}