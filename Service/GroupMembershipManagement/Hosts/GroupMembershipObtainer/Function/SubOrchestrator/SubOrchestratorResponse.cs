// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class SubOrchestratorResponse
    {
        public List<AzureADUser> Users { get; set; }
        public SyncStatus Status { get; set; }
    }
}
