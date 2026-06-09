// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Models;
using Models.AzureMaintenance;
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

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("AzureMaintenance.OrchestratorFunction");
            var runId = context.NewGuid();

            using (logger.BeginRunIdScope(runId))
            {
                logger.OrchestratorStarted(nameof(OrchestratorFunction), context.CurrentUtcDateTime);

                await context.CallActivityAsync<int>(nameof(PurgeOldHistoryFunction), null);

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
                            var processTask = context.CallActivityAsync(nameof(PurgingEmailSenderFunction), new PurgingEmailSenderRequest
                            {
                                RunId = runId,
                                SyncJob = backUpJob,
                                NotificationType = Models.Notifications.NotificationMessageType.InactiveSyncJobNotification
                            });
                            processingTasks.Add(processTask);
                        }
                        await Task.WhenAll(processingTasks);
                    }

                    var jobsApproachingDeletion = await context.CallActivityAsync<List<SyncJob>>(nameof(GetWarningJobs), null);
                    if (jobsApproachingDeletion != null && jobsApproachingDeletion.Count > 0)
                    {
                        var warningEmailTasks = new List<Task>();
                        foreach (var jobApproachingDeletion in jobsApproachingDeletion)
                        {
                            var warningTask = context.CallActivityAsync(nameof(WarningEmailSenderFunction), new WarningEmailSenderRequest
                            {
                                RunId = runId,
                                SyncJob = jobApproachingDeletion,
                                NotificationType = Models.Notifications.NotificationMessageType.JobPurgingWarningNotification
                            });
                            warningEmailTasks.Add(warningTask);
                        }
                        await Task.WhenAll(warningEmailTasks);
                    }

                    await context.CallActivityAsync<int>(nameof(RemoveBackUpsFunction), null);
                }

                logger.OrchestratorCompleted(nameof(OrchestratorFunction), context.CurrentUtcDateTime);
            }
        }
    }
}
