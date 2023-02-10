// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;

namespace Hosts.JobTrigger
{
    public class JobStatusUpdaterRequest
    {
        public SyncStatus Status { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}