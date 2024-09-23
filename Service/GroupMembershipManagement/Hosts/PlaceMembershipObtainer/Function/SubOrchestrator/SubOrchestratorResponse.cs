// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Models
{
    public class SubOrchestratorResponse
    {
        public List<AzureADUser> Users { get; set; } 
        public SyncStatus Status { get; set; }
    }
}

