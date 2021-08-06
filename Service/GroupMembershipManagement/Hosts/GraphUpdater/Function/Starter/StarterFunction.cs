// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Entities;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Polly;
using Repositories.Contracts;
using Services.Contracts;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private const int MAX_RETRY_ATTEMPTS = 20;
        private const int FIRST_RETRY_DELAY_IN_SECONDS = 10;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IServiceBusMessageService _messageService = null;


        public StarterFunction(ILoggingRepository loggingRepository, IServiceBusMessageService messageService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task RunAsync(
        [ServiceBusTrigger("%membershipQueueName%", Connection = "differenceQueueConnection", IsSessionsEnabled = true)] Message message,
        [DurableClient] IDurableOrchestrationClient starter, IMessageSession messageSession)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(StarterFunction) + " function started" });

            var messageDetails = _messageService.GetMessageProperties(message);
            var graphRequest = new GraphUpdaterFunctionRequest()
            {
                Message = Encoding.UTF8.GetString(messageDetails.Body),
                MessageSessionId = messageDetails.SessionId,
                MessageLockToken = messageDetails.LockToken
            };

            var source = new CancellationTokenSource();
            var cancellationToken = source.Token;
            var renew = RenewMessages(messageSession, cancellationToken);

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), graphRequest);

            List<GroupMembershipMessage> completedGroupMembershipMessages = null;
            bool isLastMessage = false;

            OrchestrationRuntimeStatus[] orchestratorRuntimeStatusCodesWorthRetrying = {
                OrchestrationRuntimeStatus.ContinuedAsNew,
                OrchestrationRuntimeStatus.Running,
                OrchestrationRuntimeStatus.Pending
            };

            DurableOrchestrationStatus result = null;

            /*Understanding the Retry policy
            We have a lot of sub-second sync execution so the first query would ensure we cater to those queries
            We also have a lot of syncs that take less than 10 seconds. Having a exponetial backoff 1.25^1 would mean we would be waiting 90 seconds per sync instead of 10 seconds.
            Hence the logic to ensure retryAttempt 1 is done after 10 seconds. Following this we go back to the exponetial backoff.
             */

            var retryPolicy = Policy
                .HandleResult<DurableOrchestrationStatus>(status => orchestratorRuntimeStatusCodesWorthRetrying.Contains(status.RuntimeStatus))
                .WaitAndRetryAsync(
                    MAX_RETRY_ATTEMPTS,
                    retryAttempt =>
                    {
                        if (retryAttempt == 1)
                            return TimeSpan.FromSeconds(FIRST_RETRY_DELAY_IN_SECONDS);
                        else
                            return TimeSpan.FromMinutes(Math.Pow(1.25, retryAttempt - 1));
                    }
                );

            await retryPolicy.ExecuteAsync(async () =>
            {
                result = await starter.GetStatusAsync(instanceId);
                return result;
            });

            if (result.RuntimeStatus == OrchestrationRuntimeStatus.Failed || result.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Error: Status of instance {result.InstanceId} is {result.RuntimeStatus}. The error message is : {result.Output}" });

                // stop renewing the message session
                source.Cancel();
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Instance processing completed for {result.InstanceId}" });
                var orchestratorResponseOutput = JsonConvert.DeserializeObject<GroupMembershipMessageResponse>(result.Output.ToString());
                completedGroupMembershipMessages = orchestratorResponseOutput.CompletedGroupMembershipMessages;
                isLastMessage = orchestratorResponseOutput.ShouldCompleteMessage;
            }

            renew = RenewMessages(messageSession, cancellationToken);

            if (isLastMessage)
            {
                var completedLockTokens = completedGroupMembershipMessages.Select(x => x.LockToken);
                await messageSession.CompleteAsync(completedLockTokens);
                await messageSession.CloseAsync();
                source.Cancel();
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(StarterFunction) + " function completed" });
        }

        private static readonly TimeSpan _waitBetweenRenew = TimeSpan.FromSeconds(30);
        private async Task RenewMessages(IMessageSession messageSession, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await messageSession.RenewSessionLockAsync();
                await Task.Delay(_waitBetweenRenew);
            }
        }
    }
}