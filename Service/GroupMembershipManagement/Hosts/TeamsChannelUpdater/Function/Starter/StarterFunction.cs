// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class StarterFunction
    {
        private const string SubscriptionName = "TeamsChannelUpdater";
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // A Service Bus message on the membership updaters topic starts a short-lived, per-job
        // orchestration and the function returns immediately (start-and-forget). This replaces the
        // former eternal QueueMessageOrchestratorFunction (fixed instanceId + ContinueAsNew), which on
        // Flex Consumption could be caught mid-flight by a pod recycle and left stuck Running forever.
        // Once the orchestration is durably scheduled it survives worker recycles on its own.
        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusMembershipUpdatersTopic%", SubscriptionName, Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            _logger.FunctionStarted(nameof(StarterFunction));

            MembershipHttpRequest request = null;
            if (message?.Body != null)
            {
                request = JsonSerializer.Deserialize<MembershipHttpRequest>(Encoding.UTF8.GetString(message.Body));
            }

            if (request?.SyncJob == null)
            {
                _logger.EmptyMessageReceived(message?.MessageId ?? "(null)");
                _logger.FunctionCompleted(nameof(StarterFunction));
                return;
            }

            using var scope = _logger.BeginSyncJobScope(request.SyncJob);

            _logger.ProcessingMessage(request.GroupId);

            // Deterministic instance id keyed on RunId so a redelivered Service Bus message dedupes to a
            // single orchestration instance instead of double-applying membership changes. There is one
            // membership updaters message per Teams channel run, so RunId uniquely identifies the work.
            var runId = request.SyncJob.RunId ?? Guid.Empty;
            var instanceId = runId != Guid.Empty
                ? $"{nameof(OrchestratorFunction)}_{runId}"
                : $"{nameof(OrchestratorFunction)}_{request.GroupId}_{message.SequenceNumber}";

            try
            {
                var orchestratorStatus = await starter.GetInstanceAsync(instanceId);

                if (orchestratorStatus == null)
                {
                    _logger.CallingOrchestrator(instanceId);
                    await starter.ScheduleNewOrchestrationInstanceAsync(
                        nameof(OrchestratorFunction),
                        request,
                        new StartOrchestrationOptions { InstanceId = instanceId });
                }
                else if (orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Running
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Pending)
                {
                    // A prior delivery of this run is already in flight; skip to avoid a concurrent duplicate.
                    _logger.DuplicateOrchestrationInFlight(instanceId, orchestratorStatus.RuntimeStatus.ToString());
                }
                else
                {
                    // Completed / Failed / Terminated: this run was already processed.
                    _logger.MessageAlreadyProcessed(message.MessageId, message.SequenceNumber, orchestratorStatus.RuntimeStatus.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.StarterServiceBusError(ex, ex.Message);
                throw;
            }

            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}

