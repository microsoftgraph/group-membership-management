// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class SubOrchestratorResponse
    {
        public SyncStatus Status { get; set; }
        public string FilePath { get; set; }
    }
}
