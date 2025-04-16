// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace GraphUpdater.QueueMessageOrchestrator
{
    public class QueueMessageOrchestratorRequest
    {
        public string TopicName { get; set; }
        public string SubscriptionName { get; set; }
        public bool IsMultiLaneEnabled { get; set; }
        public string LaneSize { get; set; }
    }
}
