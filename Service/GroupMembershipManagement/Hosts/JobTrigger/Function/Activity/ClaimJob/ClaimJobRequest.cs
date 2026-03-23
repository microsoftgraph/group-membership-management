// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.JobTrigger
{
    public class ClaimJobRequest
    {
        public SyncStatus Status { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
