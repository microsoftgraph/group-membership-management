// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using MessageSplitter.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Repositories.Contracts;

namespace Hosts.MessageSplitter
{

    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly MembershipUpdaters _membershipUpdaters;

        public OrchestratorFunction(
                ILoggingRepository loggingRepository,
                MembershipUpdaters membershipUpdaters)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<OrchestratorRequest>();
            var runId = request.MembershipRequest.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
            {
                Message = new LogMessage { Message = $"Processing message {request.MessageId}", RunId = runId }
            });

            try
            {
                await context.CallActivityAsync(nameof(TopicMessageSenderFunction), new TopicMessageSenderRequest
                {
                    MembershipRequest = request.MembershipRequest,
                    MessageSize = _membershipUpdaters.AvailableInstances[request.UpdaterType][request.CurrentLaneSize].MessageSize,
                    SubscriptionName = request.SubscriptionName,
                    InstanceToUse = request.InstanceToUse,
                    LaneSize = request.CurrentLaneSize
                });
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                {
                    Message = new LogMessage { Message = $"Unexpected error: {ex.Message}", RunId = runId }
                });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                new JobStatusUpdaterRequest
                {
                    Status = SyncStatus.Error,
                    SyncJob = request.MembershipRequest.SyncJob
                });

                throw;
            }
            finally
            {
                _loggingRepository.RemoveSyncJobProperties(runId);
            }
        }
    }
}