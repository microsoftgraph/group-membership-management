// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Storage.Queues;
using Hosts.WebApi;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<OperationsBackgroundService> _logger;
        private readonly IServiceProvider _services;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ServiceBusAdministrationClient _sbAdministrationClient;
        private readonly HttpClient _httpClient;

        public OperationsBackgroundService(OperationsSettings operationsSettings,
                                           IResourceManagerService resourceManagerService,
                                           IOperationsTaskQueue backgroundTaskQueue,
                                           ILogger<OperationsBackgroundService> logger,
                                           IServiceProvider services)
        {
            _operationsSettings = operationsSettings ?? throw new ArgumentNullException(nameof(operationsSettings));
            _resourceManagerService = resourceManagerService ?? throw new ArgumentNullException(nameof(resourceManagerService));
            _backgroundTaskQueue = backgroundTaskQueue ?? throw new ArgumentNullException(nameof(backgroundTaskQueue));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _services = services ?? throw new ArgumentNullException(nameof(services));

            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

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
                            var internalTQs = ClearInternalTablesAndQueuesAsync(cancellationToken);
                            var maQueue = ClearQueueAsync(_operationsSettings.MembershipAggregatorQueue, cancellationToken);
                            await Task.WhenAll(internalTQs, maQueue);
                            await ClearAllTopicsAsync(cancellationToken);
                            await ResetJobsInProgressAsync();
                            await SetStatusAsync(ServiceStatuses.Stopped, operationDetails.RequestorId);
                            await CallJobSchedulerAsync(operationDetails.RequestorId, cancellationToken);
                            await StartGMMAsync(operationDetails, cancellationToken);

                            _logger.OperationsResetCompleted();
                        }
                        else if (operationDetails.Operation == Operations.Stop)
                        {
                            await _resourceManagerService.StopWebSitesAsync(operationDetails.RequestorId, cancellationToken);
                            var internalTQs = ClearInternalTablesAndQueuesAsync(cancellationToken);
                            var maQueue = ClearQueueAsync(_operationsSettings.MembershipAggregatorQueue, cancellationToken);
                            await Task.WhenAll(internalTQs, maQueue);
                            await ClearAllTopicsAsync(cancellationToken);
                            await ResetJobsInProgressAsync();
                            await SetStatusAsync(ServiceStatuses.Stopped, operationDetails.RequestorId);
                            _logger.OperationsStopCompleted();
                        }
                        else if (operationDetails.Operation == Operations.Start)
                        {
                            await StartGMMAsync(operationDetails, cancellationToken);
                            _logger.OperationsStartCompleted();
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
                    _logger.OperationsLoopUnexpectedError(ex);

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
            // Dynamic + Stall logic implementation
            // Capture initial remaining (if provider supplied) to scale iteration cap.
            long? startRemaining = null;
            if (remainingMessageCountProvider != null)
            {
                try { startRemaining = await remainingMessageCountProvider(cancellationToken); } catch { /* ignore */ }
            }

            int expectedIterations = startRemaining.HasValue
                                        ? (int)Math.Ceiling(startRemaining.Value / (double)batchSize)
                                        : 200; // Fallback when we can't read remaining (no provider, e.g., per-session drain).
                                               // 200 => maxIterations 800 supports ~80K messages at batchSize 100;
            const int OvershootFactor = 3;      // Allow multiple passes worth of iterations.
            const int IterationBuffer = 200;    // Flat buffer to tolerate tail partial batches.
            int maxIterations = expectedIterations * OvershootFactor + IterationBuffer;

            const int StagnantEmptyLimit = 50;  // Number of consecutive empty polls with unchanged remaining before declaring stall.
            int stagnantEmptyIterations = 0;
            long totalReceived = 0;
            long lastTotalReceived = 0;
            long? lastRemaining = startRemaining;
            int emptyStreak = 0;
            int iteration = 0;

            _logger.DrainStarting(entityName, startRemaining?.ToString() ?? "?", expectedIterations, maxIterations);

            while (true)
            {
                iteration++;
                IReadOnlyList<ServiceBusReceivedMessage> messages;
                try
                {
                    messages = await receiver.ReceiveMessagesAsync(batchSize, maxWaitTime, cancellationToken);
                }
                catch (ServiceBusException sbEx) when (sbEx.IsTransient)
                {
                    _logger.DrainTransientReceiveError(entityName, sbEx.Reason.ToString(), iteration);
                    continue;
                }

                if (messages.Count == 0)
                {
                    emptyStreak++;

                    if (remainingMessageCountProvider != null)
                    {
                        long remaining;
                        try { remaining = await remainingMessageCountProvider(cancellationToken); }
                        catch { remaining = -1; }

                        if (remaining >= 0)
                        {
                            if (remaining == 0 && emptyStreak >= consecutiveEmptyThreshold)
                            {
                                _logger.DrainCompleted(entityName, iteration, totalReceived);
                                break;
                            }

                            if (remaining > 0 && emptyStreak == 1)
                            {
                                _logger.DrainRuntimeIndicatesRemaining(entityName, remaining);
                            }

                            // Stagnation detection: remaining not changing while empties accumulate
                            if (lastRemaining.HasValue && remaining == lastRemaining && totalReceived == lastTotalReceived)
                                stagnantEmptyIterations++;
                            else
                                stagnantEmptyIterations = 0;

                            lastRemaining = remaining;

                            if (stagnantEmptyIterations >= StagnantEmptyLimit)
                            {
                                _logger.DrainStallDetected(entityName, remaining, stagnantEmptyIterations, totalReceived);
                                break;
                            }
                        }
                        else if (emptyStreak >= consecutiveEmptyThreshold)
                        {
                            // Can't read runtime; trust empties
                            _logger.DrainCompletedRuntimeUnavailable(entityName, iteration, totalReceived);
                            break;
                        }
                    }
                    else if (emptyStreak >= consecutiveEmptyThreshold)
                    {
                        _logger.DrainCompletedNoRuntimeVerification(entityName, iteration, totalReceived);
                        break;
                    }
                }
                else
                {
                    totalReceived += messages.Count;
                    emptyStreak = 0;
                    stagnantEmptyIterations = 0; // progress resets stagnation

                    if (iteration % 10 == 0)
                    {
                        if (remainingMessageCountProvider != null)
                        {
                            long remaining;
                            try { remaining = await remainingMessageCountProvider(cancellationToken); }
                            catch { remaining = -1; }
                            _logger.DrainProgressWithRemaining(entityName, messages.Count, totalReceived, remaining >= 0 ? remaining.ToString() : "?");
                        }
                        else
                        {
                            _logger.DrainProgress(entityName, messages.Count, totalReceived);
                        }
                    }
                }

                lastTotalReceived = totalReceived;

                // Dynamic iteration cap safeguard
                if (iteration >= maxIterations)
                {
                    _logger.DrainIterationCapReached(entityName, iteration, maxIterations, totalReceived, lastRemaining?.ToString() ?? "?");
                    break;
                }
            }

            _logger.PurgeSummary(entityName, totalReceived, iteration);
            return totalReceived;
        }

        private async Task ClearQueueAsync(string queueName, CancellationToken cancellationToken)
        {
            _logger.ClearQueueStarted(queueName);

            var receiver = _serviceBusClient.CreateReceiver(queueName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

            // Provide runtime properties for stronger assurance
            Func<CancellationToken, Task<long>> remainingProvider = async ct =>
            {
                var runtime = await _sbAdministrationClient.GetQueueRuntimePropertiesAsync(queueName, ct);
                return runtime.Value.ActiveMessageCount;
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
            _logger.ClearQueueCompleted(queueName);
        }

        private async Task ClearTopicAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            _logger.ClearTopicStarted(topicName, subscriptionName);

            await ClearDeferredMessagesAsync(topicName, subscriptionName, cancellationToken);

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
            
            _logger.ClearTopicCompleted(topicName, subscriptionName);
        }

        private async Task ClearDeferredMessagesAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            // Check remaining count - if messages remain after draining active, they're likely deferred
            var runtime = await _sbAdministrationClient.GetSubscriptionRuntimePropertiesAsync(topicName, subscriptionName, cancellationToken);
            if (runtime.Value.ActiveMessageCount == 0)
            {
                return;
            }

            var entityName = $"{topicName}/{subscriptionName}";
            _logger.DeferredMessagesProcessing(entityName, runtime.Value.ActiveMessageCount);

            // Configuration for high-throughput processing
            const int peekBatchSize = 100;
            const int completeBatchSize = 50;          // Smaller batches for completion to avoid lock timeouts
            const int maxEmptyPeeks = 5;
            const int maxRetries = 3;
            const int progressLogInterval = 500;

            var receiver = _serviceBusClient.CreateReceiver(
                topicName,
                subscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode = ServiceBusReceiveMode.PeekLock,
                    PrefetchCount = 0  // Disable prefetch for deferred message handling
                });

            try
            {
                long totalDeferredCleared = 0;
                long totalFailed = 0;
                int emptyPeekStreak = 0;
                long fromSequenceNumber = 0;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                while (!cancellationToken.IsCancellationRequested)
                {
                    IReadOnlyList<ServiceBusReceivedMessage> peekedMessages;

                    try
                    {
                        peekedMessages = await receiver.PeekMessagesAsync(peekBatchSize, fromSequenceNumber, cancellationToken);
                    }
                    catch (ServiceBusException sbEx) when (sbEx.IsTransient)
                    {
                        _logger.DeferredPeekTransientError(entityName, sbEx.Reason.ToString());
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                        continue;
                    }

                    if (peekedMessages.Count == 0)
                    {
                        emptyPeekStreak++;
                        if (emptyPeekStreak >= maxEmptyPeeks)
                            break;

                        // Small delay before retrying empty peek
                        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                        continue;
                    }

                    emptyPeekStreak = 0;
                    fromSequenceNumber = peekedMessages[^1].SequenceNumber + 1;

                    // Collect deferred message sequence numbers
                    var deferredSequenceNumbers = peekedMessages
                        .Where(m => m.State == ServiceBusMessageState.Deferred)
                        .Select(m => m.SequenceNumber)
                        .ToList();

                    if (deferredSequenceNumbers.Count == 0)
                        continue;

                    // Process in smaller batches with retry logic
                    foreach (var batch in deferredSequenceNumbers.Chunk(completeBatchSize))
                    {
                        var (completed, failed) = await ProcessDeferredBatchWithRetryAsync(
                            receiver,
                            batch,
                            entityName,
                            maxRetries,
                            cancellationToken);

                        totalDeferredCleared += completed;
                        totalFailed += failed;

                        // Progress logging for high-volume scenarios
                        if (totalDeferredCleared > 0 && totalDeferredCleared % progressLogInterval == 0)
                        {
                            var rate = totalDeferredCleared / stopwatch.Elapsed.TotalSeconds;
                            _logger.DeferredProgress(entityName, totalDeferredCleared, totalFailed, rate);
                        }
                    }
                }

                stopwatch.Stop();

                if (totalDeferredCleared > 0 || totalFailed > 0)
                {
                    _logger.DeferredSummary(entityName, totalDeferredCleared, totalFailed, stopwatch.Elapsed.TotalSeconds);
                }
            }
            finally
            {
                await receiver.CloseAsync(cancellationToken);
            }
        }

        private async Task<(long completed, long failed)> ProcessDeferredBatchWithRetryAsync(
            ServiceBusReceiver receiver,
            long[] sequenceNumbers,
            string entityName,
            int maxRetries,
            CancellationToken cancellationToken)
        {
            long completed = 0;
            long failed = 0;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var deferredMessages = await receiver.ReceiveDeferredMessagesAsync(sequenceNumbers, cancellationToken);

                    var completionTasks = deferredMessages.Select(async message =>
                    {
                        try
                        {
                            await receiver.CompleteMessageAsync(message, cancellationToken);
                            return (success: true, seqNum: message.SequenceNumber);
                        }
                        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageLockLost)
                        {
                            // Lock lost - message will need to be retried or will eventually expire
                            return (success: false, seqNum: message.SequenceNumber);
                        }
                    });

                    var results = await Task.WhenAll(completionTasks);
                    completed += results.Count(r => r.success);
                    failed += results.Count(r => !r.success);

                    return (completed, failed);
                }
                catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageNotFound)
                {
                    // Messages may have expired or been processed - not a failure
                    _logger.DeferredMessagesNotFound(entityName);
                    return (completed, failed);
                }
                catch (ServiceBusException ex) when (ex.IsTransient && attempt < maxRetries)
                {
                    // Exponential backoff for transient errors
                    var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100);
                    _logger.DeferredBatchTransientError(entityName, attempt, maxRetries, ex.Reason.ToString());
                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception ex) when (attempt == maxRetries)
                {
                    _logger.DeferredBatchFailed(entityName, maxRetries, ex);
                    failed += sequenceNumbers.Length;
                    return (completed, failed);
                }
            }

            return (completed, failed);
        }

        private async Task ClearSessionEnabledTopicAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            _logger.ClearSessionTopicStarted(topicName, subscriptionName);

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
                    _logger.AcceptSessionFailed(topicName, subscriptionName, ex);
                    break;
                }

                if (sessionReceiver == null)
                {
                    break;
                }

                sessionsCleared++;
                var sessionEntityName = $"Topic {topicName}/Subscription {subscriptionName}/Session {sessionReceiver.SessionId}";
                _logger.ClearSessionMessages(sessionReceiver.SessionId, topicName, subscriptionName);

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
                    _logger.ClearSessionFailed(sessionReceiver.SessionId, topicName, subscriptionName, ex);
                }
                finally
                {
                    try { await sessionReceiver.CloseAsync(cancellationToken); } catch { }
                }
            }

            _logger.ClearSessionTopicCompleted(topicName, subscriptionName, sessionsCleared);
        }

        private async Task ClearAllTopicsAsync(CancellationToken cancellationToken)
        {
            _logger.ClearAllTopicsStarting();

            var topics = _sbAdministrationClient.GetTopicsAsync();
            var clearTopicTasks = new List<Task>();
            var maxDegreeOfParallelism = 3;
            using var sem = new SemaphoreSlim(maxDegreeOfParallelism);

            await foreach (var topic in topics)
            {
                var subscriptions = _sbAdministrationClient.GetSubscriptionsAsync(topic.Name);
                await foreach (var subscription in subscriptions)
                {
                    clearTopicTasks.Add(RunLimitedAsync(
                        sem,
                        cancellationToken,
                        subscription.RequiresSession
                            ? () => ClearSessionEnabledTopicAsync(topic.Name, subscription.SubscriptionName, cancellationToken)
                            : () => ClearTopicAsync(topic.Name, subscription.SubscriptionName, cancellationToken)
                    ));
                }
            }

            await Task.WhenAll(clearTopicTasks);
        }

        private Task RunLimitedAsync(SemaphoreSlim sem, CancellationToken ct, Func<Task> work) =>
        Task.Run(async () =>
        {
            await sem.WaitAsync(ct);
            try { await work(); }
            finally { sem.Release(); }
        }, ct);

        private async Task ClearInternalTablesAndQueuesAsync(CancellationToken cancellationToken)
        {
            _logger.ClearInternalTablesAndQueuesStarting();

            if (string.IsNullOrWhiteSpace(_operationsSettings.FunctionsStorageAccountName))
            {
                _logger.LogError("Settings:functionsStorageAccountName is not configured; internal tables/queues cannot be cleared.");

                throw new InvalidOperationException("Missing required setting: Settings:functionsStorageAccountName");
            }

            await ClearInternalQueuesAsync(_operationsSettings.FunctionsStorageAccountName);
            await DeleteInternalTablesAsync(_operationsSettings.FunctionsStorageAccountName);

            // Deleting a table takes at least 40 seconds, so we need to wait a bit before restarting the functions
            // reference: https://learn.microsoft.com/en-us/rest/api/storageservices/delete-table#remarks
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);

            _logger.LogInformation("Clearing function's internal tables and queues completed.");
        }

        private async Task DeleteInternalTablesAsync(string storageAccountName)
        {
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

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

                    _logger.InternalTableDeleted(table.Name, storageAccountName, "SharedFunctionsStorageAccount");
                }
                catch (Exception ex)
                {
                    _logger.InternalTableDeleteFailed(table.Name, storageAccountName, "SharedFunctionsStorageAccount", ex);
                }
            }
        }

        private async Task ClearInternalQueuesAsync(string storageAccountName)
        {
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

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

                    _logger.InternalQueueCleared(queue.Name, storageAccountName, "SharedFunctionsStorageAccount");

                }
                catch (Exception ex)
                {
                    _logger.InternalQueueClearFailed(queue.Name, storageAccountName, "SharedFunctionsStorageAccount", ex);
                }
            }
        }

        private async Task ResetJobsInProgressAsync()
        {
            _logger.ResetJobsInProgressStarting();

            try
            {
                using var scope = _services.CreateScope();
                var databaseSyncJobsRepository = scope.ServiceProvider.GetRequiredService<IDatabaseSyncJobsRepository>();

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var count = await databaseSyncJobsRepository.BulkResetJobStatusAsync(
                    SyncStatus.InProgress, SyncStatus.Idle, cts.Token);

                _logger.ResetJobsInProgressCompleted(count);
            }
            catch (Exception ex)
            {
                _logger.ResetJobsInProgressFailed(ex);
            }
        }

        private async Task CallJobSchedulerAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            try
            {
                var retryPolicy = Policy.HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                    .WaitAndRetryAsync(5,
                                       retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                                       onRetry: (response, timespan, retry, context) =>
                                       {
                                           _logger.JobSchedulerCallRetry(response.Result.StatusCode, retry);
                                       });

                await _resourceManagerService.StartWebSiteAsync(requestorId, $"{_operationsSettings.ComputeResourceGroupName}-JobScheduler", cancellationToken);
                _logger.JobSchedulerCalling();
                var jobSchedulerUrl = $"{_operationsSettings.JobSchedulerFunctionBaseUrl}/api/PipelineInvocationStarterFunction?code={_operationsSettings.JobSchedulerFunctionKey}";

                // Acquire token for JobScheduler function app with platform authentication
                var credential = new DefaultAzureCredential(DefaultAzureCredential.DefaultEnvironmentVariableName);
                var tokenRequestContext = new TokenRequestContext(new[] { $"api://{_operationsSettings.FunctionAuthAppClientId}/.default" });
                var accessToken = await credential.GetTokenAsync(tokenRequestContext, cancellationToken);
                
                await retryPolicy.ExecuteAsync(async () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, jobSchedulerUrl);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Token);
                    request.Content = new StringContent(JsonSerializer.Serialize(new { DelayForDeploymentInMinutes = 5 }), Encoding.UTF8, "application/json");
                    var response = await _httpClient.SendAsync(request);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.JobSchedulerResponse(response.StatusCode, responseContent);
                    return response;
                });
            }
            catch (Exception ex)
            {
                _logger.JobSchedulerFailed(ex);
            }
        }
    }
}
