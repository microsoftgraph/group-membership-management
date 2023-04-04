// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.OwnershipReader
{
    public class OrchestratorRequest
    {
        public SyncJob SyncJob { get; set; }
        public int TotalParts { get; set; }
        public int CurrentPart { get; set; }
        public bool Exclusionary { get; set; }
    }
}
