// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace SqlMembershipObtainer
{
    public class OrchestratorResponse
    {
        public SyncStatus Status { get; set; }
        public string FilePath { get; set; }
    }
}
