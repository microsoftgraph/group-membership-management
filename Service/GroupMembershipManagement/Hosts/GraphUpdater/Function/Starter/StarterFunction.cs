// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private const int MAX_RETRY_ATTEMPTS = 20;
        private const int FIRST_RETRY_DELAY_IN_SECONDS = 10;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IServiceBusMessageService _messageService = null;
        private readonly IConfiguration _configuration = null;
        private static ConcurrentDictionary<string, SessionTracker> _sessionsTracker = new ConcurrentDictionary<string, SessionTracker>();

        public StarterFunction(ILoggingRepository loggingRepository, IServiceBusMessageService messageService, IConfiguration configuraion)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _configuration = configuraion ?? throw new ArgumentNullException(nameof(configuraion));
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

            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphRequest.Message);

            SetSessionTracker(messageDetails, groupMembership);

            var source = new CancellationTokenSource();
            var renew = RenewMessages(starter, messageSession, source, messageDetails.MessageId);
            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), graphRequest);
            var completedGroupMembershipMessages = default(List<GroupMembershipMessage>);
            var isLastMessage = false;
            var orchestratorRuntimeStatusCodesWorthRetrying = new OrchestrationRuntimeStatus[]
            {
                OrchestrationRuntimeStatus.ContinuedAsNew,
                OrchestrationRuntimeStatus.Running,
                OrchestrationRuntimeStatus.Pending
            };

            var result = default(DurableOrchestrationStatus);

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
        private async Task RenewMessages(
            IDurableOrchestrationClient starter,
            IMessageSession messageSession,
            CancellationTokenSource cancellationToken,
            string messageId)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var action = await TrackLastMessageTimeout(starter, messageSession, messageId);
                if (action == SessionTrackerAction.Stop)
                {
                    break;
                }

                await messageSession.RenewSessionLockAsync();
                await Task.Delay(_waitBetweenRenew);
            }
        }

        private void SetSessionTracker(MessageInformation messageDetails, GroupMembership groupMembership)
        {
            if (_sessionsTracker.ContainsKey(messageDetails.SessionId))
            {
                var sessionTracker = _sessionsTracker[messageDetails.SessionId];
                var lockTokens = sessionTracker.LockTokens;

                lockTokens.Add(messageDetails.LockToken);
                _sessionsTracker.TryUpdate(messageDetails.SessionId,
                                            new SessionTracker
                                            {
                                                LastAccessTime = DateTime.UtcNow,
                                                LatestMessageId = messageDetails.MessageId,
                                                RunId = groupMembership.RunId,
                                                LockTokens = lockTokens,
                                                JobPartitionKey = groupMembership.SyncJobPartitionKey,
                                                JobRowKey = groupMembership.SyncJobRowKey,
                                                ReceivedLastMessage = groupMembership.IsLastMessage
                                            },
                                            sessionTracker
                                           );
            }
            else
            {
                _sessionsTracker.TryAdd(messageDetails.SessionId,
                                        new SessionTracker
                                        {
                                            LastAccessTime = DateTime.UtcNow,
                                            LatestMessageId = messageDetails.MessageId,
                                            RunId = groupMembership.RunId,
                                            LockTokens = new List<string> { messageDetails.LockToken },
                                            JobPartitionKey = groupMembership.SyncJobPartitionKey,
                                            JobRowKey = groupMembership.SyncJobRowKey,
                                            ReceivedLastMessage = groupMembership.IsLastMessage
                                        });
            }
        }

        private async Task<SessionTrackerAction> TrackLastMessageTimeout(IDurableOrchestrationClient starter, IMessageSession messageSession, string messageId)
        {
            if (_sessionsTracker.TryGetValue(messageSession.SessionId, out var sessionTracker))
            {
                if (sessionTracker.ReceivedLastMessage)
                {
                    return SessionTrackerAction.Continue;
                }

                if (messageId != sessionTracker.LatestMessageId)
                {
                    return SessionTrackerAction.Stop;
                }

                int.TryParse(_configuration["GraphUpdater:LastMessageWaitTimeout"], out int timeOut);
                timeOut = timeOut == 0 ? 10 : timeOut;

                var elapsedTime = DateTime.UtcNow - sessionTracker.LastAccessTime;
                if (elapsedTime.TotalMinutes >= timeOut)
                {
                    _sessionsTracker.Remove(messageSession.SessionId, out _);
                    await messageSession.CompleteAsync(sessionTracker.LockTokens);
                    await messageSession.CloseAsync();

                    var cancelationRequest = new GraphUpdaterFunctionRequest
                    {
                        IsCancelationRequest = true,
                        MessageSessionId = messageSession.SessionId,
                        Message = JsonConvert.SerializeObject(new GroupMembership
                        {
                            SyncJobPartitionKey = sessionTracker.JobPartitionKey,
                            SyncJobRowKey = sessionTracker.JobRowKey,
                            RunId = sessionTracker.RunId
                        }),
                    };

                    await starter.StartNewAsync(nameof(OrchestratorFunction), cancelationRequest);
                    return SessionTrackerAction.Stop;
                }
            }

            return SessionTrackerAction.Continue;
        }
    }
}