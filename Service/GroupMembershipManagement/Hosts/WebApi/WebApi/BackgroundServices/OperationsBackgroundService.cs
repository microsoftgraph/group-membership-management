// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Storage.Queues;
using Models;
using Newtonsoft.Json;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using Services.WebApi.Contracts;
using System.Diagnostics.CodeAnalysis;
using System.Text;
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

            _serviceBusClient = new ServiceBusClient(operationsSettings.ServiceBusFQN, new DefaultAzureCredential());
            _sbAdministrationClient = new ServiceBusAdministrationClient(_operationsSettings.ServiceBusFQN, new DefaultAzureCredential());
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var operationDetails = await _backgroundTaskQueue.DequeueAsync();

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
            }
        }

        private async Task ClearQueueAsync(string queueName, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing queue {queueName}"
            });

            var receiver = _serviceBusClient.CreateReceiver(queueName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

            while (true)
            {
                var messages = await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(10), cancellationToken);
                if (messages == null || messages.Count == 0)
                {
                    break;
                }
            }

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

            while (true)
            {
                var messages = await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(10), cancellationToken);
                if (messages == null || messages.Count == 0)
                {
                    break;
                }
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Clearing topic {topicName} subscription {subscriptionName} completed"
            });
        }

        private async Task ClearAllTopicsAsync(CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Clearing topics and their subscriptions..."
            });

            var subscriptions = await Task.WhenAll(GetSubscriptionsAsync(_operationsSettings.MembershipUpdatersTopic),
                                                   GetSubscriptionsAsync(_operationsSettings.SyncJobTopic));

            var membershipUpdaterSubscriptions = subscriptions.FirstOrDefault(s => s.topicName == _operationsSettings.MembershipUpdatersTopic).subscriptions;
            var syncJobSubscriptions = subscriptions.FirstOrDefault(s => s.topicName == _operationsSettings.SyncJobTopic).subscriptions;
            var clearTopicTasks = new List<Task>();

            foreach (var subscription in membershipUpdaterSubscriptions)
            {
                clearTopicTasks.Add(ClearTopicAsync(_operationsSettings.MembershipUpdatersTopic, subscription, cancellationToken));
            }

            foreach (var subscription in syncJobSubscriptions)
            {
                clearTopicTasks.Add(ClearTopicAsync(_operationsSettings.SyncJobTopic, subscription, cancellationToken));
            }

            await Task.WhenAll(clearTopicTasks);
        }

        private async Task<(string topicName, List<string> subscriptions)> GetSubscriptionsAsync(string topicName)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Getting subscriptions for topic {topicName}"
            });

            var subscriptionNames = new List<string>();
            await foreach (var subscription in _sbAdministrationClient.GetSubscriptionsAsync(topicName))
            {
                subscriptionNames.Add(subscription.SubscriptionName);
            }

            return (topicName, subscriptionNames);
        }

        private async Task ClearInternalTablesAndQueuesAsync(CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Clearing function's internal tables and queues..."
            });

            // FunctionName, StorageAccountName
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
            var tableServiceClient = new TableServiceClient(new Uri($"https://{storageAccountName}.table.core.windows.net"), new DefaultAzureCredential());
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
            var queueClient = new QueueServiceClient(new Uri($"https://{storageAccountName}.queue.core.windows.net"), new DefaultAzureCredential());
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
                    request.Content = new StringContent(JsonConvert.SerializeObject(new { DelayForDeploymentInMinutes = 5 }), Encoding.UTF8, "application/json");
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
