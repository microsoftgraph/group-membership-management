// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.SyncJobUpdater
{
    public class OrchestratorRequest
    {
        public SyncJob SyncJob { get; set; }
        public string Status { get; set; }
    }
}
