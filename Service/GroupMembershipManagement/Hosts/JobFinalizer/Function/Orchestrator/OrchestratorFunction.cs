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
using Hosts.JobFinalizer;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Repositories.Contracts.InjectConfig;
using Models.Notifications;

namespace Hosts.JobFinalizer
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _log;

        public OrchestratorFunction(
            ILoggingRepository loggingRepository,
            )
        {
            _log = loggingRepository;

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

                if (!string.Equals(syncjobStatus, "unknown", StringComparison.OrdinalIgnoreCase))
                {
                    SyncStatus status = (SyncStatus)Enum.Parse(typeof(SyncStatus), syncjobStatus, true);

                }
                else
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"{syncJob.TargetOfficeGroupId} pass an unknown status. Marking job as {SyncStatus.Error}."}, VerbosityLevel.DEBUG);
                    return 
                }

                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);
                    
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = status });
                    
                if (!context.IsReplaying)
                    _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId, DynamicProperties = syncJob.ToDictionary() }, VerbosityLevel.DEBUG);
            }
        }
    }
}