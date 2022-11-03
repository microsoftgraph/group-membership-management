// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class SubOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient = null;

        public SubOrchestratorFunction(ILoggingRepository loggingRepository, TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(SubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {

            var syncJob = context.GetInput<SyncJob>();

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            try
            {
                if (!string.IsNullOrWhiteSpace(syncJob.Query))
                {
                    var query = JToken.Parse(syncJob.Query);

                    if (!context.IsReplaying)
                    {
                        TrackExclusionaryEvent(context, syncJob);
                    }
                }
                else
                {
                    if (!context.IsReplaying)
                        _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Job query is empty for job RowKey:{syncJob.RowKey}", RunId = syncJob.RunId });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                    return;
                }
            }
            catch (JsonReaderException)
            {
                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"JSON query is not valid for job RowKey:{syncJob.RowKey}", RunId = syncJob.RunId });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                return;
            }

            var groupInformation = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), syncJob);
            if (string.IsNullOrEmpty(groupInformation.Name))
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest { Status = SyncStatus.DestinationGroupNotFound, SyncJob = syncJob });
                return;
            }

            await context.CallActivityAsync(nameof(EmailSenderFunction), groupInformation);
            var canWriteToGroup = await context.CallActivityAsync<bool>(nameof(GroupVerifierFunction), syncJob);
            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                            new JobStatusUpdaterRequest { Status = canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, SyncJob = syncJob });

            if (canWriteToGroup)
            {
                await context.CallActivityAsync(nameof(TopicMessageSenderFunction), syncJob);
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
        }

        private void TrackExclusionaryEvent(IDurableOrchestrationContext context, SyncJob syncJob)
        {
            var query = JArray.Parse(syncJob.Query);
            var queryTypes = query.Select(x => new
            {
                exclusionary = x["exclusionary"] != null ? (bool)x["exclusionary"] : false
            }).ToList();

            var exclusionaryEvent = new Dictionary<string, string>
            {
                { "DestinationGroupObjectId", syncJob.TargetOfficeGroupId.ToString() },
                { "TotalNumberOfSourceParts", queryTypes.Count.ToString() },
                { "NumberOfExclusionarySourceParts", queryTypes.Where(g => g.exclusionary).Count().ToString() }
            };
            _telemetryClient.TrackEvent("ExclusionarySourcePartsCount", exclusionaryEvent);
        }
    }
}