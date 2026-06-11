// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ServiceBus;

namespace Hosts.SyncJobUpdater
{
    public class OrchestratorRequest
    {
        public JobStatusUpdateQueueMessage Message { get; set; }
    }
}
