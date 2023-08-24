// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using JobTrigger.Activity.EmailSender;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.IdentityModel.Tokens;
using Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.SyncJobsRepository;
using Services;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class SubOrchestratorFunction
    {
        private const string EmailSubject = "EmailSubject";
        private const string SyncStartedEmailBody = "SyncStartedEmailBody";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";
        private const string DisabledJobEmailSubject = "DisabledJobEmailSubject";

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
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context, ExecutionContext executionContext)
        {

            var syncJob = context.GetInput<SyncJob>();

            if (!string.IsNullOrEmpty(syncJob.Status) && syncJob.Status == SyncStatus.StuckInProgress.ToString())
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.ErroredDueToStuckInProgress, SyncJob = syncJob });
                return;
            }

            if (!context.IsReplaying) { TrackJobsStartedEvent(syncJob.RunId); }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = (Guid)syncJob.RunId,
                    Message = $"{nameof(SubOrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

            var frequency = await context.CallActivityAsync<int>(nameof(JobTrackerFunction), syncJob);

            DestinationObject destinationObject = null;

            try
            {
                var parsedAndValidatedDestination = await context.CallActivityAsync<(bool IsValid, DestinationObject DestinationObject)>(nameof(ParseAndValidateDestinationFunction), syncJob);
                destinationObject =  parsedAndValidatedDestination.DestinationObject;
               
                if (!parsedAndValidatedDestination.IsValid)
                {

                    await context.CallActivityAsync(nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            RunId = (Guid)syncJob.RunId,
                            Message = $"Destination query is empty or missing required fields for job RowKey:{syncJob.RowKey}"
                        });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.DestinationQueryNotValid, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationQueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }
            }
            catch (JsonReaderException)
            {

                await context.CallActivityAsync(nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            RunId = (Guid)syncJob.RunId,
                            Message = $"Destination query is not valid for job RowKey:{syncJob.RowKey}"
                        });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.DestinationQueryNotValid, SyncJob = syncJob });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationQueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                return;
            }


            if (!context.IsReplaying)
            {
                if (syncJob.Status == SyncStatus.Idle.ToString())
                {
                    TrackIdleJobsEvent(frequency, destinationObject.Value.ObjectId);
                }
                else if (syncJob.Status == SyncStatus.InProgress.ToString())
                {
                    TrackInProgressJobsEvent(frequency, destinationObject.Value.ObjectId, syncJob.RunId);
                }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(syncJob.Query))
                {
                    var query = JToken.Parse(syncJob.Query);
                }
                else
                {
              
                    await context.CallActivityAsync(nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            RunId = (Guid)syncJob.RunId,
                            Message = $"Source query is empty for job RowKey:{syncJob.RowKey}"
                        });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }
            }
            catch (JsonReaderException)
            {
             
                await context.CallActivityAsync(nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            RunId = (Guid)syncJob.RunId,
                            Message = $"Source query is not valid for job RowKey:{syncJob.RowKey}"
                        });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                return;
            }

            if (!context.IsReplaying)
            {
                TrackExclusionaryEvent(syncJob.Query, destinationObject.Value.ObjectId);
            }

            var groupInformation = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), syncJob);

            if (string.IsNullOrEmpty(groupInformation.Name))
            {
                await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                new EmailSenderRequest
                                                {
                                                    SyncJobGroup = groupInformation,
                                                    EmailSubjectTemplateName = DisabledJobEmailSubject,
                                                    EmailContentTemplateName = SyncDisabledNoGroupEmailBody,
                                                    AdditionalContentParams = new[]
                                                    {
                                                        destinationObject.Value.ToString(),
                                                        _emailSenderAndRecipients.SyncDisabledCCAddresses
                                                    },
                                                    FunctionDirectory = executionContext.FunctionAppDirectory
                                                });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest { Status = SyncStatus.DestinationGroupNotFound, SyncJob = syncJob });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationGroupNotFound, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                return;
            }

            if (syncJob.LastRunTime == SqlDateTime.MinValue.Value)
                await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                new EmailSenderRequest
                                                {
                                                    SyncJobGroup = groupInformation,
                                                    EmailSubjectTemplateName = EmailSubject,
                                                    EmailContentTemplateName = SyncStartedEmailBody,
                                                    AdditionalContentParams = new[]
                                                    {
                                                            destinationObject.Value.ToString(),
                                                            groupInformation.Name,
                                                            _emailSenderAndRecipients.SupportEmailAddresses,
                                                            _gmmResources.LearnMoreAboutGMMUrl,
                                                            syncJob.Requestor
                                                    },
                                                    FunctionDirectory = executionContext.FunctionAppDirectory
                                                });

            var canWriteToGroup = await context.CallActivityAsync<bool>(nameof(GroupVerifierFunction), new GroupVerifierRequest()
            {
                SyncJob = syncJob,
                FunctionDirectory = executionContext.FunctionAppDirectory
            });

            var statusValue = SyncStatus.StuckInProgress;

            if (!canWriteToGroup)
            {
                statusValue = SyncStatus.NotOwnerOfDestinationGroup;
            }
            else if (syncJob.Status == SyncStatus.Idle.ToString())
            {
                    statusValue = SyncStatus.InProgress;
            }

            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = statusValue, SyncJob = syncJob });

            if (canWriteToGroup)
            {
                await context.CallActivityAsync(nameof(TopicMessageSenderFunction), syncJob);
            }
            else
            {
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.NotOwnerOfDestinationGroup, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
               new LoggerRequest
               {
                   RunId = (Guid)syncJob.RunId,
                   Message = $"{nameof(SubOrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                   Verbosity = VerbosityLevel.DEBUG
               });

        }

        private void TrackJobsStartedEvent(Guid? runId)
        {
            var jobsStartedEvent = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() }
            };
            _telemetryClient.TrackEvent("NumberOfJobsStarted", jobsStartedEvent);
        }

        private void TrackIdleJobsEvent(int frequency, Guid destinationGroupObjectId)
        {
            var jobStarted = frequency >= 1 ? 1 : 0;

            var idleJobsEvent = new Dictionary<string, string>
            {
                { "DestinationGroupObjectId", destinationGroupObjectId.ToString() },
                { "Frequency", frequency.ToString() },
                { "JobStarted", jobStarted.ToString() }
            };

            _telemetryClient.TrackEvent("IdleJobsTracker", idleJobsEvent);
        }

        private void TrackInProgressJobsEvent(int frequency, Guid destinationGroupObjectId, Guid? runId)
        {
            var inProgressJobsEvent = new Dictionary<string, string>
            {
                { "DestinationGroupObjectId", destinationGroupObjectId.ToString() },
                { "Frequency", frequency.ToString() },
                { "RunId", runId.ToString() }
            };

            _telemetryClient.TrackEvent("InProgressJobsTracker", inProgressJobsEvent);
        }

        private void TrackExclusionaryEvent(string query, Guid destinationGroupObjectId)
        {
            var parsedQuery = JArray.Parse(query);
            var queryTypes = parsedQuery.Select(x => new
            {
                exclusionary = x["exclusionary"] != null ? (bool)x["exclusionary"] : false
            }).ToList();

            var exclusionaryEvent = new Dictionary<string, string>
            {
                { "DestinationGroupObjectId", destinationGroupObjectId.ToString() },
                { "TotalNumberOfSourceParts", queryTypes.Count.ToString() },
                { "NumberOfExclusionarySourceParts", queryTypes.Where(g => g.exclusionary).Count().ToString() }
            };
            _telemetryClient.TrackEvent("ExclusionarySourcePartsCount", exclusionaryEvent);
        }
    }
}