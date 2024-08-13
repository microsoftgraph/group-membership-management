// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Graph;
using Models;
using Models.Helpers;
using Newtonsoft.Json;
using Repositories.Contracts;
using Hosts.SyncJobUpdater;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Repositories.Contracts.InjectConfig;
using Models.Notifications;
using Hosts.SyncJobUpdater;

namespace Hosts.SyncJobUpdater
{
    public class OrchestratorFunction
    {
        public OrchestratorFunction()
        {
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context, ExecutionContext executionContext)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            if (mainRequest != null && mainRequest.SyncJob != null)
            {
                var syncJob = mainRequest.SyncJob;
                var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
                var syncjobStatus = mainRequest.Status;
                SyncStatus status;
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                        {
                            RunId = runId,
                            Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                            Verbosity = VerbosityLevel.DEBUG
                        });
                if (!string.Equals(syncjobStatus, "unknown", StringComparison.OrdinalIgnoreCase))
                {
                    status = (SyncStatus)Enum.Parse(typeof(SyncStatus), syncjobStatus, true);

                }
                else
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { RunId = runId, Message = $"{syncJob.TargetOfficeGroupId} pass an unknown status. Marking job as {SyncStatus.Error}.", Verbosity = VerbosityLevel.DEBUG });
                    return;
                }
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = status });
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { RunId = runId, Message = $"{nameof(OrchestratorFunction)} function completed", Verbosity = VerbosityLevel.DEBUG });
            }
        }
    }
}