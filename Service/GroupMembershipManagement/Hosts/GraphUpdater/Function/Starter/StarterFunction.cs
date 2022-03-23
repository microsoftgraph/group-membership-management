// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private const int MAX_RETRY_ATTEMPTS = 20;
        private const int FIRST_RETRY_DELAY_IN_SECONDS = 10;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IServiceBusMessageService _messageService = null;
        private readonly IConfiguration _configuration = null;
        TelemetryClient _telemetryClient = null;
        private SessionTracker _sessionTracker = new SessionTracker();
        private static readonly TimeSpan _waitBetweenQueuePolling = TimeSpan.FromSeconds(5);

        public StarterFunction(ILoggingRepository loggingRepository, IServiceBusMessageService messageService, IConfiguration configuration, TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task RunAsync(
        [ServiceBusTrigger("%membershipQueueName%", Connection = "differenceQueueConnection", IsSessionsEnabled = true)] Message[] messages,
        [DurableClient] IDurableOrchestrationClient starter, IMessageSession messageSession)
        {
            var runId = new Guid(messageSession.SessionId);

            // Update session tracker with needed values
            _sessionTracker.LastAccessTime = DateTime.UtcNow;
            _sessionTracker.MessagesInSession = messages.ToList();
            _sessionTracker.SessionId = messageSession.SessionId;

            var latestMessage = messages.Last();
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(Encoding.UTF8.GetString(latestMessage.Body));

            _sessionTracker.TotalMessageCountExpected = groupMembership.TotalMessageCount;
            _sessionTracker.SyncJobPartitionKey = groupMembership.SyncJobPartitionKey;
            _sessionTracker.SyncJobRowKey = groupMembership.SyncJobRowKey;

            _loggingRepository.SyncJobProperties = new Dictionary<string, string>()
            {
                {"RowKey", groupMembership.SyncJobRowKey },
                {"PartitionKey", groupMembership.SyncJobPartitionKey },
                {"TargetOfficeGroupId", groupMembership.Destination.ObjectId.ToString() },
                {"RunId", groupMembership.RunId.ToString() }
            };

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = nameof(StarterFunction) + " function started",
                RunId = runId
            });

            var source = new CancellationTokenSource();
            var renew = RenewMessages(starter, messageSession, source);

            // Obtain all messages in session
            while (_sessionTracker.MessagesInSession.Count < _sessionTracker.TotalMessageCountExpected)
            {
                if(source.IsCancellationRequested)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Error: Process was cancelled, likely due to a timeout on receiving messages from ServiceBus",
                        RunId = runId
                    });

                    return;
                }

                var moreMessages = await messageSession.ReceiveAsync(1000);

                if (moreMessages != null)
                {
                    _sessionTracker.MessagesInSession.AddRange(moreMessages);

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = "Received " + moreMessages.Count + " new messages from session with SessionId: " + _sessionTracker.SessionId,
                        RunId = runId
                    });
                }
                else
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = "Received no new messages from session with SessionId: " + _sessionTracker.SessionId,
                        RunId = runId
                    });
                }

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = "Currently received " + _sessionTracker.MessagesInSession.Count + " out of " + _sessionTracker.TotalMessageCountExpected +
                    " messages from session with SessionId: " + _sessionTracker.SessionId,
                    RunId = runId
                });

                await Task.Delay(_waitBetweenQueuePolling);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { 
                Message = "Obtained all " + _sessionTracker.MessagesInSession.Count + " messages from session with SessionId: " + _sessionTracker.SessionId,
                RunId = runId
            });

            // Update group membership with expected full membership to send over to OrchestratorFunction
            groupMembership.SourceMembers = _sessionTracker.MessagesInSession
                .SelectMany(message =>
            {
                var messageDetails = _messageService.GetMessageProperties(message);
                var membership = JsonConvert.DeserializeObject<GroupMembership>(Encoding.UTF8.GetString(messageDetails.Body));
                return membership.SourceMembers;
            }).ToList();

            var graphRequest = new GraphUpdaterFunctionRequest()
            {
                MessageSessionId = _sessionTracker.SessionId,
                IsCancelationRequest = false,
                Membership = groupMembership
            };

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), graphRequest);
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
                await _loggingRepository.LogMessageAsync(new LogMessage {
                    Message = $"Error: Status of instance {result.InstanceId} is {result.RuntimeStatus}. The error message is : {result.Output}",
                    RunId = runId
                });
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { 
                    Message = $"Instance processing completed for {result.InstanceId}",
                    RunId = runId
                });
            }
            
            var completedLockTokens = _sessionTracker.MessagesInSession.Select(message => message.SystemProperties.LockToken);

            // Complete messages in message session, close the message session and then stop renewing it
            await messageSession.CompleteAsync(completedLockTokens);

            try
            {
                await messageSession.CloseAsync();
            }
            catch(SessionLockLostException ex)
            {
                var exceptionMessage = $"Session lock lost in GraphUpdater for session with RunId: {runId}, " +
                    $"TargetOfficeGroupId: {groupMembership.Destination.ObjectId}, and {_sessionTracker.MessagesInSession.Count} messages in session.";

                var guSessionLockException = new GraphUpdaterSessionLockLostException(exceptionMessage, ex);

                _telemetryClient.TrackException(guSessionLockException, new Dictionary<string, string>()
                {
                    {"TargetOfficeGroupId", groupMembership.Destination.ObjectId.ToString() },
                    {"RunId", runId.ToString() },
                    {"MessagesInSessionCount", _sessionTracker.MessagesInSession.Count.ToString() }
                });

                throw guSessionLockException;
            }
            
            source.Cancel();

            await _loggingRepository.LogMessageAsync(new LogMessage { 
                Message = nameof(StarterFunction) + " function completed",
                RunId = runId
            });
        }

        private static readonly TimeSpan _waitBetweenRenew = TimeSpan.FromSeconds(30);
        private async Task RenewMessages(
            IDurableOrchestrationClient starter,
            IMessageSession messageSession,
            CancellationTokenSource cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var action = await TrackLastMessageTimeout(starter, messageSession, cancellationToken);
                if (action == SessionTrackerAction.Stop)
                {
                    break;
                }

                await messageSession.RenewSessionLockAsync();
                await Task.Delay(_waitBetweenRenew);
            }
        }

        private async Task<SessionTrackerAction> TrackLastMessageTimeout(IDurableOrchestrationClient starter, IMessageSession messageSession, CancellationTokenSource cancellationToken)
        {
            if (_sessionTracker.MessagesInSession.Count < _sessionTracker.TotalMessageCountExpected)
            {
                int.TryParse(_configuration["GraphUpdater:LastMessageWaitTimeout"], out int timeOut);
                timeOut = timeOut == 0 ? 10 : timeOut;

                var elapsedTime = DateTime.UtcNow - _sessionTracker.LastAccessTime;
                if (elapsedTime.TotalMinutes >= timeOut)
                {
                    var lockTokens = _sessionTracker.MessagesInSession.Select(message => message.SystemProperties.LockToken);
                    await messageSession.CompleteAsync(lockTokens);
                    await messageSession.CloseAsync();
                    cancellationToken.Cancel();

                    var cancellationRequest = new GraphUpdaterFunctionRequest
                    {
                        IsCancelationRequest = true,
                        MessageSessionId = messageSession.SessionId,
                        Membership = new GroupMembership
                        {
                            SyncJobPartitionKey = _sessionTracker.SyncJobPartitionKey,
                            SyncJobRowKey = _sessionTracker.SyncJobRowKey,
                            RunId = _sessionTracker.RunId
                        }
                    };

                    await starter.StartNewAsync(nameof(OrchestratorFunction), cancellationRequest);
                    return SessionTrackerAction.Stop;
                }
            }

            return SessionTrackerAction.Continue;
        }

        private bool IsLastMessageInSession(Message message)
        {
            var messageDetails = _messageService.GetMessageProperties(message);
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(Encoding.UTF8.GetString(messageDetails.Body));

            return groupMembership.IsLastMessage;
        }
    }
}