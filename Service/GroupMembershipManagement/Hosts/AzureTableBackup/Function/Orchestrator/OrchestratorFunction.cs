// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services.Entities;
using Services.Contracts;

namespace Hosts.AzureTableBackup
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
            var runId = Guid.NewGuid();

            await context.CallActivityAsync(
                               nameof(LoggerFunction),
                               new LoggerRequest
                               {
                                   RunId = runId,
                                   Message = $"{nameof(OrchestratorFunction)} function started at: {DateTime.UtcNow}"
                               });

            await context.CallActivityAsync(nameof(TableBackupFunction), null);

            var tablesToReview = await context.CallActivityAsync<List<ReviewAndDeleteRequest>>(
                nameof(RetrieveBackupsFunction), 
                new RetrieveBackupsRequest());

            var tablesDeleted = new Dictionary<string, int>();

            foreach (var tableToReview in tablesToReview)
            {
                var entityDeleted = await context.CallActivityAsync<bool>(nameof(ReviewAndDeleteFunction), tableToReview);
                if (entityDeleted)
                {
                    if (tablesDeleted.ContainsKey(tableToReview.TableName))
                    {
                        tablesDeleted[tableToReview.TableName] = tablesDeleted[tableToReview.TableName] + 1;
                    }
                    else
                    {
                        tablesDeleted.Add(tableToReview.TableName, 1);
                    }
                }
            }

            foreach (var item in tablesDeleted)
            {
                await context.CallActivityAsync(
                                   nameof(LoggerFunction),
                                   new LoggerRequest
                                   {
                                       RunId = runId,
                                       Message = $"Deleted {item.Value} old backups for table: {item.Key}"
                                   });
            }

            await context.CallActivityAsync(
                               nameof(LoggerFunction),
                               new LoggerRequest
                               {
                                   RunId = runId,
                                   Message = $"{nameof(OrchestratorFunction)} function completed at: {DateTime.UtcNow}"
                               });
        }
    }
}
