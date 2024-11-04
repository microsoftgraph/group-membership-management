// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;

namespace Hosts.MessageSplitter
{
    public class InstanceTracker : TaskEntity<int>
    {
        public void Set(int instanceId) => this.State = instanceId;

        public int Get() => this.State;

        [Function(nameof(InstanceTracker))]
        public static Task Run([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync<InstanceTracker>();
        }
    }
}
