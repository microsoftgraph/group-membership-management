// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace SqlMembershipObtainer
{
    public class TelemetryTrackerRequest
    {
        public SyncStatus JobStatus { get; set; }
        public ResultStatus ResultStatus { get; set; }
        public Guid? RunId { get; set; }
    }
}