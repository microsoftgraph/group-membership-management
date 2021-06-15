// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.JobTrigger
{
    public class JopStatusUpdaterRequest
    {
        public bool CanWriteToGroup { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}