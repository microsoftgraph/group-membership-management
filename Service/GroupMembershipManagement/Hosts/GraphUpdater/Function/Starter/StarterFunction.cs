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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;
using Repositories.Contracts;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly int MAX_RETRY_ATTEMPTS = 12;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task Run(
        [ServiceBusTrigger("%membershipQueueName%", Connection = "differenceQueueConnection", IsSessionsEnabled = true)] Message message,
        [DurableClient] IDurableOrchestrationClient starter, ILogger logMessage, IMessageSession messageSession)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(StarterFunction) + " function started" });

            var graphRequest = new GraphUpdaterFunctionRequest()
            {
                Message = Encoding.UTF8.GetString(message.Body),
                MessageSessionId = message.SessionId,
                MessageLockToken = message.SystemProperties.LockToken
            };

            var source = new CancellationTokenSource();
            var cancellationToken = source.Token;
            var renew = RenewMessages(messageSession, message.SystemProperties.LockToken, cancellationToken);

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), graphRequest);

            IEnumerable<GroupMembershipMessage> completedGroupMembershipMessages = null;
            bool isLastMessage = false;

            OrchestrationRuntimeStatus[] orchestratorRuntimeStatusCodesWorthRetrying = {
                OrchestrationRuntimeStatus.ContinuedAsNew,
                OrchestrationRuntimeStatus.Running,
                OrchestrationRuntimeStatus.Pending
            };

            DurableOrchestrationStatus result = null;
            var retryPolicy = Policy
                .HandleResult<DurableOrchestrationStatus>(status => orchestratorRuntimeStatusCodesWorthRetrying.Contains(status.RuntimeStatus))
                .WaitAndRetryAsync(
                    MAX_RETRY_ATTEMPTS,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                );

            await retryPolicy.ExecuteAsync(async () =>
            {
                result = await starter.GetStatusAsync(instanceId);
                return result;
            });

            if (result.RuntimeStatus == OrchestrationRuntimeStatus.Failed || result.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Error: Status of instance {result.InstanceId} is {result.RuntimeStatus}. The error message is : {result.Output}" });
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Instance processing completed for {result.InstanceId}" });
                var orchestratorResponseOutput = JsonConvert.DeserializeObject<GroupMembershipMessageResponse>(result.Output.ToString());
                completedGroupMembershipMessages = orchestratorResponseOutput.CompletedGroupMembershipMessages;
                isLastMessage = orchestratorResponseOutput.ShouldCompleteMessage;
            }

            if (isLastMessage)
            {
                var completedLockTokens = completedGroupMembershipMessages.Select(x => x.LockToken);
                await messageSession.CompleteAsync(completedLockTokens);
                await messageSession.CloseAsync();
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(StarterFunction) + " function completed" });

        }

        private static readonly TimeSpan _waitBetweenRenew = TimeSpan.FromMinutes(4);
        private async Task RenewMessages(IMessageSession messageSession, string lockToken, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await messageSession.RenewSessionLockAsync();
                await messageSession.RenewLockAsync(lockToken);
                await Task.Delay(_waitBetweenRenew);
            }
        }
    }

}