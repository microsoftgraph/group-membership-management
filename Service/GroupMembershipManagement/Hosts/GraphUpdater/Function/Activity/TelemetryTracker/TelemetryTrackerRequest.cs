// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class TelemetryTrackerRequest : GraphUpdaterRequestBase
    {
        public SyncStatus JobStatus { get; set; }
        public ResultStatus ResultStatus { get; set; }
    }
}