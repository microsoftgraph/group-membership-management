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

        public OrchestratorFunction()
        {
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

            await context.CallActivityAsync(nameof(TableBackupFunction), null);

            var reviewAndDeleteRequests = await context.CallActivityAsync<List<ReviewAndDeleteRequest>>(nameof(RetrieveBackupsFunction), null);

            var maintenanceSettings = reviewAndDeleteRequests.Select(e => e.MaintenanceSetting).Distinct();

            foreach (var maintenanceSetting in maintenanceSettings)
            {
                var reviewAndDeleteRequestsForSetting = reviewAndDeleteRequests.Where(e => e.MaintenanceSetting.Equals(maintenanceSetting)).ToList();

                var tasks = new List<Task<bool>>();

                foreach(var requestToReview in reviewAndDeleteRequestsForSetting)
                {
                    var task = context.CallActivityAsync<bool>(nameof(ReviewAndDeleteFunction), requestToReview);
                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);

                var backupsDeleted = tasks.Where(t => t.Result == true).Count();

                await context.CallActivityAsync(
                                   nameof(LoggerFunction),
                                   new LoggerRequest
                                   {
                                       RunId = runId,
                                       Message = $"Deleted {backupsDeleted} old backups for table: {maintenanceSetting.SourceTableName}"
                                   });
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
