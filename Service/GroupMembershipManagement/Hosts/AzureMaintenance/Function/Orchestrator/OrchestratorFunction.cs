// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Models;
using Services.Contracts;

namespace Hosts.AzureMaintenance
{
    public class OrchestratorFunction
    {
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig = null;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;

        public OrchestratorFunction(IHandleInactiveJobsConfig handleInactiveJobsConfig,
            IThresholdNotificationConfig thresholdNotificationConfig,
            IAzureMaintenanceService azureMaintenanceService)
        {
            _handleInactiveJobsConfig = handleInactiveJobsConfig;
            _thresholdNotificationConfig = thresholdNotificationConfig;
            _azureMaintenanceService = azureMaintenanceService;
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var runId = context.NewGuid();

            await context.CallActivityAsync(
                               nameof(LoggerFunction),
                               new LoggerRequest
                               {
                                   RunId = runId,
                                   Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                                   Verbosity = VerbosityLevel.DEBUG
                               });

            if (_handleInactiveJobsConfig.HandleInactiveJobsEnabled)
            {
                var inactiveSyncJobs = await context.CallActivityAsync<List<SyncJob>>(nameof(ReadSyncJobsFunction), null);
                var backUpJobs = await context.CallActivityAsync<List<PurgedSyncJob>>(nameof(BackUpInactiveJobsFunction), inactiveSyncJobs);

                if (inactiveSyncJobs != null && inactiveSyncJobs.Count > 0 && inactiveSyncJobs.Count == backUpJobs.Count)
                {
                    await context.CallActivityAsync(nameof(RemoveInactiveJobsFunction), inactiveSyncJobs);

                    var processingTasks = new List<Task>();
                    foreach (var backUpJob in backUpJobs)
                    {
                        var processTask = context.CallActivityAsync(nameof(EmailSenderFunction), new EmailSenderRequest
                        {
                            RunId = runId,
                            SyncJob = backUpJob,
                            NotificationType = Models.Notifications.NotificationMessageType.InactiveSyncJobNotification
                        });
                        processingTasks.Add(processTask);
                    }
                    await Task.WhenAll(processingTasks);
                }

                await context.CallActivityAsync<int>(nameof(RemoveBackUpsFunction), null);
            }

            await context.CallActivityAsync(
                               nameof(LoggerFunction),
                               new LoggerRequest
                               {
                                   RunId = runId,
                                   Message = $"{nameof(OrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                                   Verbosity = VerbosityLevel.DEBUG
                               });
        }
    }
}
