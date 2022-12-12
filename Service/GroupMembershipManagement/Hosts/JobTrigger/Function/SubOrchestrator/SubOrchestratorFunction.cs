// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using JobTrigger.Activity.EmailSender;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class SubOrchestratorFunction
    {
        private const string SyncStartedEmailBody = "SyncStartedEmailBody";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";

        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGMMResources _gmmResources;

        public SubOrchestratorFunction(ILoggingRepository loggingRepository,
                                       TelemetryClient telemetryClient,
                                       IEmailSenderRecipient emailSenderAndRecipients,
                                       IGMMResources gmmResources)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _emailSenderAndRecipients = emailSenderAndRecipients;
        }

        [FunctionName(nameof(SubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {

            var syncJob = context.GetInput<SyncJob>();

            if (!context.IsReplaying) { TrackJobsStartedEvent(syncJob.RunId); }

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
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }
            }
            catch (JsonReaderException)
            {
                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"JSON query is not valid for job RowKey:{syncJob.RowKey}", RunId = syncJob.RunId });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                return;
            }

            var groupInformation = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), syncJob);
            if (string.IsNullOrEmpty(groupInformation.Name))
            {
                await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                new EmailSenderRequest
                                                {
                                                    SyncJobGroup = groupInformation,
                                                    EmailTemplateName = SyncDisabledNoGroupEmailBody,
                                                    AdditionalContentParams = new[]
                                                    {
                                                        syncJob.TargetOfficeGroupId.ToString(),
                                                        _emailSenderAndRecipients.SyncDisabledCCAddresses
                                                    }
                                                });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest { Status = SyncStatus.DestinationGroupNotFound, SyncJob = syncJob });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationGroupNotFound, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                return;
            }

            if (syncJob.LastRunTime == DateTime.FromFileTimeUtc(0))
                await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                new EmailSenderRequest
                                                {
                                                    SyncJobGroup = groupInformation,
                                                    EmailTemplateName = SyncStartedEmailBody,
                                                    AdditionalContentParams = new[]
                                                    {
                                                            groupInformation.Name,
                                                            syncJob.TargetOfficeGroupId.ToString(),
                                                            _emailSenderAndRecipients.SupportEmailAddresses,
                                                            _gmmResources.LearnMoreAboutGMMUrl,
                                                            syncJob.Requestor
                                                    }
                                                });


            var canWriteToGroup = await context.CallActivityAsync<bool>(nameof(GroupVerifierFunction), syncJob);
            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                            new JobStatusUpdaterRequest { Status = canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, SyncJob = syncJob });

            if (canWriteToGroup)
            {
                await context.CallActivityAsync(nameof(TopicMessageSenderFunction), syncJob);
            }
            else
            {
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.NotOwnerOfDestinationGroup, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
        }

        private void TrackJobsStartedEvent(Guid? runId)
        {
            var jobsStartedEvent = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() }
            };
            _telemetryClient.TrackEvent("NumberOfJobsStarted", jobsStartedEvent);
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