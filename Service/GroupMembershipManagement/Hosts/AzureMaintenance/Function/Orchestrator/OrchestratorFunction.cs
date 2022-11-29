// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services.Entities;
using System.Linq;
using Repositories.Contracts;

namespace Hosts.AzureMaintenance
{
    public class OrchestratorFunction
    {
        private readonly List<AzureMaintenanceJob> _maintenanceSettings = null;

        public OrchestratorFunction(List<AzureMaintenanceJob> maintenanceSettings)
        {
            _maintenanceSettings = maintenanceSettings;
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



            //if (!_maintenanceSettings.Any())
            //{

            //    await context.CallActivityAsync(
            //                       nameof(LoggerFunction),
            //                       new LoggerRequest
            //                       {
            //                           RunId = runId,
            //                           Message = $"No maintenance settings have been found.",
            //                           Verbosity = VerbosityLevel.DEBUG
            //                       });
            //    return;
            //}

            /*
            * In the Azure Maintenance process, we do the following for each maintenance job / setting:
            * 1. BackupFunction: Backup any relevant tables / blobs
            * 2. RetrieveBackupsFunction: Retrieve all tables / blobs to be reviewed and cleaned if necessary
            * 3. ReviewAndDeleteFunction: Review tables / blobs and clean / delete as needed
           */
            // Currently, only table backups are supported, but blob backups can be enabled here in the future
            //var tableBackupSettings = _maintenanceSettings.Where(setting =>
            //{
            //    return setting.Backup && setting.SourceStorageSetting.StorageType == StorageType.Table && setting.DestinationStorageSetting.StorageType == StorageType.Table;
            //});

            //var backupTasks = new List<Task>();
            //foreach (var tableBackupSetting in tableBackupSettings)
            //{
            //    backupTasks.Add(context.CallActivityAsync(nameof(BackupFunction), tableBackupSetting));
            //}

            //await Task.WhenAll(backupTasks);

            //foreach (var maintenanceSetting in _maintenanceSettings)
            //{
            //    if (maintenanceSetting.Cleanup)
            //    {

            //        await context.CallActivityAsync(
            //                            nameof(LoggerFunction),
            //                            new LoggerRequest
            //                            {
            //                                RunId = runId,
            //                                Message = $"Cleaning up old backups for {maintenanceSetting.SourceStorageSetting.StorageType.ToString()}: {maintenanceSetting.SourceStorageSetting.TargetName}"
            //                            });

            //        var reviewAndDeleteRequests = await context.CallActivityAsync<List<ReviewAndDeleteRequest>>(nameof(RetrieveBackupsFunction), maintenanceSetting);

            //        var cleanupTasks = new List<Task<bool>>();

            //        foreach (var requestToReview in reviewAndDeleteRequests)
            //        {
            //            var task = context.CallActivityAsync<bool>(nameof(ReviewAndDeleteFunction), requestToReview);
            //            cleanupTasks.Add(task);
            //        }

            //        await Task.WhenAll(cleanupTasks);

            //        var backupsDeleted = cleanupTasks.Where(t => t.Result == true).Count();

            //        await context.CallActivityAsync(
            //                            nameof(LoggerFunction),
            //                            new LoggerRequest
            //                            {
            //                                RunId = runId,
            //                                Message = $"Deleted {backupsDeleted} old backups for {maintenanceSetting.SourceStorageSetting.StorageType}: {maintenanceSetting.SourceStorageSetting.TargetName}"
            //                            });
            //    }
            //}

            var inactiveSyncJobs = await context.CallActivityAsync<List<SyncJob>>(nameof(ReadSyncJobsFunction), null);
            var countOfBackUpJobs = await context.CallActivityAsync<int>(nameof(BackUpInactiveJobsFunction), inactiveSyncJobs);

            if (inactiveSyncJobs != null && inactiveSyncJobs.Count > 0 && inactiveSyncJobs.Count == countOfBackUpJobs)
            {
                await context.CallActivityAsync<int>(nameof(RemoveInactiveJobsFunction), inactiveSyncJobs);

                var processingTasks = new List<Task>();
                foreach (var inactiveSyncJob in inactiveSyncJobs)
                {
                    var processTask = context.CallActivityAsync(nameof(SendEmailFunction), inactiveSyncJob);
                    processingTasks.Add(processTask);
                }
                await Task.WhenAll(processingTasks);
            }

            await context.CallActivityAsync<List<string>>(nameof(RemoveBackUpsFunction), null);

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
