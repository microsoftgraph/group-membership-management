// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{

    public class OrchestratorFunction
    {
        private readonly MembershipUpdaters _membershipUpdaters;

        public OrchestratorFunction(
                MembershipUpdaters membershipUpdaters)
        {
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<OrchestratorRequest>();
            var logger = context.CreateReplaySafeLogger("MessageSplitter.OrchestratorFunction");
            var updaterType = request.UpdaterType;
            var instanceTrackerEntityId = new EntityInstanceId(nameof(InstanceTracker), request.CurrentLaneSize);
            var subscription = _membershipUpdaters.AvailableInstances[request.UpdaterType][request.CurrentLaneSize];

            using (logger.BeginSyncJobScope(request.MembershipRequest.SyncJob))
            {
                logger.ProcessingMessageByOrchestrator(request.MessageId, context.InstanceId);

                try
                {
                    await context.CallActivityAsync(nameof(TopicMessageSenderFunction), new TopicMessageSenderRequest
                    {
                        MembershipRequest = request.MembershipRequest,
                        MessageSize = _membershipUpdaters.AvailableInstances[request.UpdaterType][request.CurrentLaneSize].MessageSize,
                        SubscriptionName = request.SubscriptionName,
                        InstanceToUse = 1,
                        LaneSize = request.CurrentLaneSize
                    });
                }
                catch (Exception ex)
                {
                    logger.OrchestratorUnexpectedException(ex);

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                    new JobStatusUpdaterRequest
                    {
                        Status = SyncStatus.Error,
                        SyncJob = request.MembershipRequest.SyncJob
                    });

                    throw;
                }
            }
        }
    }
}