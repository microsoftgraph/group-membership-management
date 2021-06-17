// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.JobTrigger
{
    public class JobStatusUpdaterRequest
    {
        public bool CanWriteToGroup { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}