// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.ServiceBus
{
    public class MessageSplitterCompletionSignal
    {
        public MessageSplitterCompletionSignal(Guid runId, string laneSize)
        {
            RunId = runId;
            LaneSize = laneSize;
        }

        public Guid RunId { get; set; }
        public string LaneSize { get; set; }
    }
}
