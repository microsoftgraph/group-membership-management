// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.SignalR;
using Models;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using WebApi.Models;

namespace WebApi.BackgroundServices
{
    [ExcludeFromCodeCoverage]
    public class OperationsBackgroundService : BackgroundService
    {
        private readonly OperationsSettings _operationsSettings;
        private readonly IResourceManagerService _resourceManagerService;
        private readonly IOperationsTaskQueue _backgroundTaskQueue;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IServiceProvider _services;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ServiceBusAdministrationClient _sbAdministrationClient;
        private readonly HttpClient _httpClient;

        public OperationsBackgroundService(OperationsSettings operationsSettings,
                                           IResourceManagerService resourceManagerService,
                                           IOperationsTaskQueue backgroundTaskQueue,
                                           ILoggingRepository loggingRepository,
                                           IServiceProvider services)
        {
            _operationsSettings = operationsSettings ?? throw new ArgumentNullException(nameof(operationsSettings));
            _resourceManagerService = resourceManagerService ?? throw new ArgumentNullException(nameof(resourceManagerService));
            _backgroundTaskQueue = backgroundTaskQueue ?? throw new ArgumentNullException(nameof(backgroundTaskQueue));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _services = services ?? throw new ArgumentNullException(nameof(services));

            TokenCredential credential;
#if DEBUG
            credential = new DefaultAzureCredential();
#else
            credential = new ManagedIdentityCredential();
#endif

            _serviceBusClient = new ServiceBusClient(
                                        operationsSettings.ServiceBusFQN,
                                        credential,
                                        new ServiceBusClientOptions
                                        {
                                            RetryOptions = new ServiceBusRetryOptions
                                            {
                                                TryTimeout = TimeSpan.FromSeconds(30),
                                            }
                                        });

            _sbAdministrationClient = new ServiceBusAdministrationClient(_operationsSettings.ServiceBusFQN, credential);
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            OperationDetails? operationDetails = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    operationDetails = await _backgroundTaskQueue.DequeueAsync();

                    if (operationDetails != null)
                    {
                        if (operationDetails.Operation == Operations.Reset)
                        {
                            await _resourceManagerService.StopWebSitesAsync(operationDetails.RequestorId, cancellationToken);
                            await ClearInternalTablesAndQueuesAsync(cancellationToken);
                            await ClearQueueAsync(_operationsSettings.MembershipAggregatorQueue, cancellationToken);
                            await ClearAllTopicsAsync(cancellationToken);
                            await ResetJobsInProgressAsync();
                            await SetStatusAsync(ServiceStatuses.Stopped, operationDetails.RequestorId);
                            await CallJobSchedulerAsync(operationDetails.RequestorId, cancellationToken);
                            await StartGMMAsync(operationDetails, cancellationToken);

                            await _loggingRepository
                                    .LogMessageAsync(new LogMessage
                                    {
                                        Message = "Reset operation completed."
                                    });
                        }
                        else if (operationDetails.Operation == Operations.Stop)
                        {
                            await _resourceManagerService.StopWebSitesAsync(operationDetails.RequestorId, cancellationToken);
                            await ClearInternalTablesAndQueuesAsync(cancellationToken);
                            await ClearQueueAsync(_operationsSettings.MembershipAggregatorQueue, cancellationToken);
                            await ClearAllTopicsAsync(cancellationToken);
                            await SetStatusAsync(ServiceStatuses.Stopped, operationDetails.RequestorId);
                            await _loggingRepository
                                    .LogMessageAsync(new LogMessage
                                    {
                                        Message = "Stop operation completed."
                                    });
                        }
                        else if (operationDetails.Operation == Operations.Start)
                        {
                            await StartGMMAsync(operationDetails, cancellationToken);
                            await _loggingRepository
                                    .LogMessageAsync(new LogMessage
                                    {
                                        Message = "Start operation completed."
                                    });
                        }
                        else if (operationDetails.Operation == Operations.Reschedule)
                        {
                            await CallJobSchedulerAsync(operationDetails.RequestorId, cancellationToken);
                            await SetStatusAsync(ServiceStatuses.Running, operationDetails.RequestorId);
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // No op, handle cancellation
                }
                catch (Exception ex)
                {
                    await _loggingRepository
                            .LogMessageAsync(new LogMessage
                            {
                                Message = $"Unexpected error in {nameof(OperationsBackgroundService)}\n{ex.Message}"
                            });

                    await SetStatusAsync(ServiceStatuses.Error, operationDetails?.RequestorId ?? Guid.Empty);
                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                }
            }
        }

        private async Task StartGMMAsync(OperationDetails operationDetails, CancellationToken cancellationToken)
        {
            await _resourceManagerService.StartWebSitesAsync(operationDetails.RequestorId, cancellationToken);
            await SetStatusAsync(ServiceStatuses.Running, operationDetails.RequestorId);
        }

        private async Task SetStatusAsync(ServiceStatuses status, Guid requestorId)
        {
            using (var scope = _services.CreateScope())
            {
                var scopedStatusRepository = scope.ServiceProvider.GetRequiredService<IServiceStatusRepository>();
                await scopedStatusRepository.SetServiceStatusAsync(status, requestorId);
                var signalRHubContext = scope.ServiceProvider.GetRequiredService<IHubContext<SignalRService>>();
                await signalRHubContext.Clients.All.SendAsync("ServiceStatusChanged", status);
            }
        }

        private async Task<long> DrainReceiverAsync(
            ServiceBusReceiver receiver,
            Func<CancellationToken, Task<long>>? remainingMessageCountProvider,
            string entityName,
            int batchSize,
            int consecutiveEmptyThreshold,
            TimeSpan maxWaitTime,
            CancellationToken cancellationToken)
        {
            long totalReceived = 0;
            int emptyStreak = 0;
            int iteration = 0;

            while (true)
            {
                iteration++;
                var messages = await receiver.ReceiveMessagesAsync(batchSize, maxWaitTime, cancellationToken);
                if (messages.Count == 0)
                {
                    emptyStreak++;

                    if (remainingMessageCountProvider != null)
                    {
                        var remaining = await remainingMessageCountProvider(cancellationToken);
                        if (remaining == 0 && emptyStreak >= consecutiveEmptyThreshold)
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{entityName} drained after {iteration} iterations. Total received: {totalReceived}" });
                            break;
                        }

                        if (remaining > 0 && emptyStreak == 1)
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{entityName} runtime indicates {remaining} messages remain after an empty batch; continuing..." });
                        }
                    }
                    else if (emptyStreak >= consecutiveEmptyThreshold)
                    {
                        // No runtime provider; trust consecutive empties
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{entityName} drained (no runtime verification) after {iteration} iterations. Total received: {totalReceived}" });
                        break;
                    }
                }
                else
                {
                    totalReceived += messages.Count;
                    emptyStreak = 0;

                    if (iteration % 10 == 0)
                    {
                        if (remainingMessageCountProvider != null)
                        {
                            var remaining = await remainingMessageCountProvider(cancellationToken);
                            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{entityName} progress: received {messages.Count} (total {totalReceived}). Remaining (approx): {remaining}" });
                        }
                        else
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{entityName} progress: received {messages.Count} (total {totalReceived})." });
                        }
                    }
                }
            }

            return totalReceived;
        }

        private async Task ClearQueueAsync(string queueName, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing queue {queueName}"
            });

            var receiver = _serviceBusClient.CreateReceiver(queueName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

            // Provide runtime properties for stronger assurance
            Func<CancellationToken, Task<long>> remainingProvider = async ct =>
            {
                var runtime = await _sbAdministrationClient.GetQueueRuntimePropertiesAsync(queueName, ct);
                return runtime.Value.ActiveMessageCount + runtime.Value.ScheduledMessageCount;
            };

            await DrainReceiverAsync(
                receiver,
                remainingProvider,
                entityName: $"Queue {queueName}",
                batchSize: 100,
                consecutiveEmptyThreshold: 3,
                maxWaitTime: TimeSpan.FromSeconds(10),
                cancellationToken: cancellationToken);

            await receiver.CloseAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing queue {queueName} completed"
            });
        }

        private async Task ClearTopicAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing topic {topicName} subscription {subscriptionName}"
            });

            var receiver = _serviceBusClient.CreateReceiver(topicName, subscriptionName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

            // Use subscription runtime properties for verification (Scheduled count not exposed for subscriptions)
            Func<CancellationToken, Task<long>> remainingProvider = async ct =>
            {
                var runtime = await _sbAdministrationClient.GetSubscriptionRuntimePropertiesAsync(topicName, subscriptionName, ct);
                // Active messages are what we can drain here.
                return runtime.Value.ActiveMessageCount;
            };

            await DrainReceiverAsync(
                receiver,
                remainingProvider,
                entityName: $"Topic {topicName}/Subscription {subscriptionName}",
                batchSize: 100,
                consecutiveEmptyThreshold: 2, // can be smaller for topics
                maxWaitTime: TimeSpan.FromSeconds(10),
                cancellationToken: cancellationToken);

            await receiver.CloseAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing topic {topicName} subscription {subscriptionName} completed"
            });
        }

        private async Task ClearSessionEnabledTopicAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing (session-enabled) topic {topicName} subscription {subscriptionName}"
            });

            int sessionsCleared = 0;
            while (true)
            {
                ServiceBusSessionReceiver? sessionReceiver = null;
                try
                {
                    sessionReceiver = await _serviceBusClient.AcceptNextSessionAsync(
                        topicName,
                        subscriptionName,
                        new ServiceBusSessionReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete },
                        cancellationToken);
                }
                catch (ServiceBusException sbEx) when (sbEx.Reason == ServiceBusFailureReason.ServiceTimeout)
                {
                    break; // no more sessions
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Failed to accept next session for {topicName}/{subscriptionName}.\n{ex}" });
                    break;
                }

                if (sessionReceiver == null)
                {
                    break;
                }

                sessionsCleared++;
                var sessionEntityName = $"Topic {topicName}/Subscription {subscriptionName}/Session {sessionReceiver.SessionId}";
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Clearing session {sessionReceiver.SessionId} for {topicName}/{subscriptionName}" });

                try
                {
                    // No runtime per-session, rely on consecutive empties only
                    await DrainReceiverAsync(
                        sessionReceiver,
                        remainingMessageCountProvider: null,
                        entityName: sessionEntityName,
                        batchSize: 100,
                        consecutiveEmptyThreshold: 2,
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Error while clearing session {sessionReceiver.SessionId} for {topicName}/{subscriptionName}.\n{ex}" });
                }
                finally
                {
                    try { await sessionReceiver.CloseAsync(cancellationToken); } catch { }
                }
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing (session-enabled) topic {topicName} subscription {subscriptionName} completed. Sessions processed: {sessionsCleared}"
            });
        }

        private async Task ClearAllTopicsAsync(CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Clearing topics and their subscriptions..."
            });

            var topics = _sbAdministrationClient.GetTopicsAsync();
            var clearTopicTasks = new List<Task>();

            await foreach (var topic in topics)
            {
                var subscriptions = _sbAdministrationClient.GetSubscriptionsAsync(topic.Name);
                await foreach (var subscription in subscriptions)
                {
                    if (subscription.RequiresSession)
                        clearTopicTasks.Add(ClearSessionEnabledTopicAsync(topic.Name, subscription.SubscriptionName, cancellationToken));
                    else
                        clearTopicTasks.Add(ClearTopicAsync(topic.Name, subscription.SubscriptionName, cancellationToken));
                }
            }

            await Task.WhenAll(clearTopicTasks);
        }

        private async Task ClearInternalTablesAndQueuesAsync(CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Clearing function's internal tables and queues..."
            });

            var storageAccounts = await _resourceManagerService.GetWebSitesStorageAccountsAsync(cancellationToken);
            foreach (var storageAccount in storageAccounts)
            {
                await ClearInternalQueuesAsync(storageAccount.Value, storageAccount.Key);
                await DeleteInternalTablesAsync(storageAccount.Value, storageAccount.Key);
            }

            // Deleting a table takes at least 40 seconds, so we need to wait a bit before restarting the functions
            // reference: https://learn.microsoft.com/en-us/rest/api/storageservices/delete-table#remarks
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
        }

        private async Task DeleteInternalTablesAsync(string storageAccountName, string functionName)
        {
            TokenCredential credential;
#if DEBUG
            credential = new DefaultAzureCredential();
#else
            credential = new ManagedIdentityCredential();
#endif

            var tableServiceClient = new TableServiceClient(new Uri($"https://{storageAccountName}.table.core.windows.net"), credential);
            var tables = tableServiceClient.QueryAsync();

            await foreach (var table in tables)
            {
                try
                {
                    if (!table.Name.EndsWith("History", StringComparison.InvariantCultureIgnoreCase) &&
                        !table.Name.EndsWith("Instances", StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    await tableServiceClient.DeleteTableAsync(table.Name);

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Deleted table {table.Name} from account {storageAccountName} used by {functionName}"
                    });
                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Failed to delete table {table.Name} from account {storageAccountName} used by {functionName}.\n{ex}"
                    });
                }
            }
        }

        private async Task ClearInternalQueuesAsync(string storageAccountName, string functionName)
        {
            TokenCredential credential;
#if DEBUG
            credential = new DefaultAzureCredential();
#else
            credential = new ManagedIdentityCredential();
#endif

            var queueClient = new QueueServiceClient(new Uri($"https://{storageAccountName}.queue.core.windows.net"), credential);
            var queues = queueClient.GetQueuesAsync();
            await foreach (var queue in queues)
            {
                try
                {
                    if (!queue.Name.Contains("-control-", StringComparison.InvariantCultureIgnoreCase) &&
                        !queue.Name.EndsWith("-workitems", StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    var individualClient = queueClient.GetQueueClient(queue.Name);
                    await individualClient.ClearMessagesAsync();

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Cleared queue {queue.Name} from account {storageAccountName} used by {functionName}"
                    });

                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Failed to clear queue {queue.Name} from account {storageAccountName} used by {functionName}.\n{ex}"
                    });
                }
            }
        }

        private async Task ResetJobsInProgressAsync()
        {
            using (var scope = _services.CreateScope())
            {
                var databaseSyncJobsRepository = scope.ServiceProvider.GetRequiredService<IDatabaseSyncJobsRepository>();
                var jobsInProgress = await databaseSyncJobsRepository.GetSyncJobsAsync(true, SyncStatus.InProgress);
                await databaseSyncJobsRepository.UpdateSyncJobsAsync(jobsInProgress, SyncStatus.Idle);
            }
        }

        private async Task CallJobSchedulerAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            try
            {
                var retryPolicy = Policy.HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                    .WaitAndRetryAsync(5,
                                       retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                                       onRetry: async (response, timespan, retry, context) =>
                                       {
                                           await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Failed to call JobScheduler (${response.Result.StatusCode}). Retrying... {retry}" });
                                       });

                await _resourceManagerService.StartWebSiteAsync(requestorId, $"{_operationsSettings.ComputeResourceGroupName}-JobScheduler", cancellationToken);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = "Calling JobScheduler..." });
                var jobSchedulerUrl = $"{_operationsSettings.JobSchedulerFunctionBaseUrl}/api/PipelineInvocationStarterFunction?code={_operationsSettings.JobSchedulerFunctionKey}";

                await retryPolicy.ExecuteAsync(async () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, jobSchedulerUrl);
                    request.Content = new StringContent(JsonSerializer.Serialize(new { DelayForDeploymentInMinutes = 5 }), Encoding.UTF8, "application/json");
                    var response = await _httpClient.SendAsync(request);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"JobScheduler response: {response.StatusCode}.\n{responseContent}" });
                    return response;
                });
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Failed to call JobScheduler.\n{ex}" });
            }
        }
    }
}
