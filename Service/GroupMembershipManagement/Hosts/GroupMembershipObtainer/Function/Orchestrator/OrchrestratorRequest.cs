// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class OrchestratorRequest
    {
        public SyncJob SyncJob { get; set; }
        public int TotalParts { get; set; }
        public int CurrentPart { get; set; }
        public bool Exclusionary { get; set; }
        public bool IsDestinationPart { get; set; }
    }
}
