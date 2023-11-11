// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.JobTrigger
{
    public class JobUpdaterRequest
    {
        public SyncStatus? Status { get; set; } = null;
        public SyncJob SyncJob { get; set; }
    }
}