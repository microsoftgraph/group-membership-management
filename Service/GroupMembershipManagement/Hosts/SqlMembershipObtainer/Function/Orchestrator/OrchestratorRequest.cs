// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace SqlMembershipObtainer
{
    public class OrchestratorRequest
    {
        public required SyncJob SyncJob { get; set; }
        public required int TotalParts { get; set; }
        public required int CurrentPart { get; set; }
        public required bool Exclusionary { get; set; }
    }
}
