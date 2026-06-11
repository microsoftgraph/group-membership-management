// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Hosts.MessageSplitter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using System;
using System.Threading.Tasks;

namespace MessageSplitter.TrackerOrchestrator
{
    public class InstanceTrackerOrchestrator
    {
        private readonly MembershipUpdaters _membershipUpdaters;

        public InstanceTrackerOrchestrator(MembershipUpdaters membershipUpdaters)
        {
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
        }

        [Function(nameof(InstanceTrackerOrchestrator))]
        public async Task<int> UpdateInstanceTrackerAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<InstanceTrackerOrchestratorRequest>();
            var instanceTrackerEntityId = new EntityInstanceId(nameof(InstanceTracker), request.CurrentLaneSize);
            var subscription = _membershipUpdaters.AvailableInstances[request.UpdaterType][request.CurrentLaneSize];
            var instanceToUse = 0;

            await using (await context.Entities.LockEntitiesAsync(instanceTrackerEntityId))
            {
                instanceToUse = await context.Entities.CallEntityAsync<int>(instanceTrackerEntityId, "Get");
                instanceToUse = (instanceToUse <= 0 || ++instanceToUse > subscription.Instances) ? 1 : instanceToUse;
                await context.Entities.CallEntityAsync(instanceTrackerEntityId, "Set", instanceToUse);
            }

            return instanceToUse;
        }
    }
}
