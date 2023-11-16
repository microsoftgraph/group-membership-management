// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.ApplicationInsights;
using System.Linq;
using Models.ServiceBus;
using TeamsChannelUpdater.Helpers;
using Repositories.Contracts.InjectConfig;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;
using Models.Entities;
using System.Text.Json;
using Services.TeamsChannelUpdater.Contracts;

namespace Hosts.TeamsChannelUpdater
{
    public class OrchestratorFunction
    {
        private const string SyncCompletedEmailBody = "SyncCompletedEmailBody";
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly IGMMResources _gmmResources = null;
        enum Metric
        {
            SyncComplete,
            SyncJobTimeElapsedSeconds
        }

        public OrchestratorFunction(ILoggingRepository loggingRepository,
            TelemetryClient telemetryClient,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task<OrchestrationRuntimeStatus> RunOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ExecutionContext executionContext)
        {
            TeamsGroupMembership groupMembership = null;
            MembershipHttpRequest graphRequest = null;
            SyncJob syncJob = null;
            var syncCompleteEvent = new SyncCompleteCustomEvent();

            graphRequest = context.GetInput<MembershipHttpRequest>();


            try
            {
                syncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                                       new JobReaderRequest
                                                       {
                                                           JobId = graphRequest.SyncJob.Id,
                                                           RunId = graphRequest.SyncJob.RunId.GetValueOrDefault()
                                                       });
                var sourceTypesCounts = JsonParser.GetQueryTypes(syncJob.Query);
                var destination = JsonParser.GetDestination(syncJob.Destination);

                syncCompleteEvent.Type = destination.Type;
                syncCompleteEvent.Destination = syncJob.Destination;
                syncCompleteEvent.SourceTypesCounts = sourceTypesCounts;
                syncCompleteEvent.RunId = syncJob.RunId.ToString();
                syncCompleteEvent.IsDryRunEnabled = false.ToString();
                syncCompleteEvent.ProjectedMemberCount = graphRequest.ProjectedMemberCount.HasValue ? graphRequest.ProjectedMemberCount.ToString() : "Not provided";

                var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                                            new FileDownloaderRequest
                                                                            {
                                                                                FilePath = graphRequest.FilePath,
                                                                                SyncJob = syncJob
                                                                            });

                JsonSerializerOptions options = new JsonSerializerOptions();
                options.Converters.Add(new AzureADTeamsUserConverter());
                groupMembership = JsonSerializer.Deserialize<TeamsGroupMembership>(fileContent, options);

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", RunId = syncJob.RunId.GetValueOrDefault(Guid.Empty), Verbosity = VerbosityLevel.DEBUG });
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                {
                    Message = $"Received membership from StarterFunction and will sync the obtained " +
                                                                                              $"{groupMembership.SourceMembers.Distinct().Count()} distinct members",
                    RunId = syncJob.RunId.GetValueOrDefault(Guid.Empty)
                });

                var isInitialSync = syncJob.LastRunTime == DateTime.FromFileTimeUtc(0);
                syncCompleteEvent.IsInitialSync = isInitialSync.ToString();
                var membersToAdd = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Add).Distinct().ToList();
                syncCompleteEvent.MembersToAdd = membersToAdd.Count.ToString();
                var membersToRemove = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Remove).Distinct().ToList();
                syncCompleteEvent.MembersToRemove = membersToRemove.Count.ToString();


                var membersAddedResponse = await context.CallSubOrchestratorAsync<TeamsChannelUpdaterSubOrchestratorResponse>(nameof(TeamsChannelUpdaterSubOrchestratorFunction),
                                CreateTeamsGroupUpdaterRequest(isInitialSync,
                                syncJob.RunId.GetValueOrDefault(Guid.Empty),
                                membersToAdd,
                                destination,
                                RequestType.Add));
                syncCompleteEvent.MembersAdded = membersAddedResponse.SuccessCount.ToString();
                syncCompleteEvent.MembersToAddNotFound = membersAddedResponse.UsersNotFound.Count.ToString();

                var membersRemovedResponse = await context.CallSubOrchestratorAsync<TeamsChannelUpdaterSubOrchestratorResponse>(nameof(TeamsChannelUpdaterSubOrchestratorFunction),
                                CreateTeamsGroupUpdaterRequest(isInitialSync,
                                syncJob.RunId.GetValueOrDefault(Guid.Empty),
                                membersToRemove,
                                destination,
                                RequestType.Remove));
                syncCompleteEvent.MembersRemoved = membersRemovedResponse.SuccessCount.ToString();
                syncCompleteEvent.MembersToRemoveNotFound = membersRemovedResponse.UsersNotFound.Count.ToString();

                if (isInitialSync)
                {
                    var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                    new GroupNameReaderRequest { RunId = groupMembership.RunId, GroupId = groupMembership.Destination.ObjectId });

                    var groupOwners = await context.CallActivityAsync<List<AzureADUser>>(nameof(GroupOwnersReaderFunction),
                                                    new GroupOwnersReaderRequest { RunId = groupMembership.RunId, GroupId = groupMembership.Destination.ObjectId });

                    var ownerEmails = string.Join(";", groupOwners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));

                    var additionalContent = new[]
                    {
                                groupMembership.Destination.ObjectId.ToString(),
                                groupName,
                                membersToAdd.Count.ToString(),
                                membersToRemove.Count.ToString(),
                                syncJob.Requestor,
                                _gmmResources.LearnMoreAboutGMMUrl,
                                _emailSenderAndRecipients.SupportEmailAddresses
                    };

                    await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                    new EmailSenderRequest
                                                    {
                                                        ToEmail = ownerEmails,
                                                        CcEmail = _emailSenderAndRecipients.SyncCompletedCCAddresses,
                                                        ContentTemplate = SyncCompletedEmailBody,
                                                        AdditionalContentParams = additionalContent,
                                                        RunId = groupMembership.RunId
                                                    });
                }


                var message = GetUsersDataMessage(groupMembership.Destination.ObjectId, membersToAdd.Count, membersToRemove.Count);
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = message, RunId = syncJob.RunId.GetValueOrDefault(Guid.Empty) });

                if (membersAddedResponse.UsersFailed.Count > 0)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                        SyncStatus.TeamsChannelError, 0, groupMembership.RunId));
                }
                else
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                        SyncStatus.Idle, 0, groupMembership.RunId));
                }

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                if (!context.IsReplaying)
                {
                    if (membersAddedResponse.SuccessCount + membersAddedResponse.UsersNotFound.Count == membersToAdd.Count &&
                        membersRemovedResponse.SuccessCount + membersRemovedResponse.UsersNotFound.Count == membersToRemove.Count)
                    {
                        TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Success");
                    }
                    else
                    {
                        TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "PartialSuccess");
                    }
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        RunId = syncJob.RunId.GetValueOrDefault(Guid.Empty),
                        Message = $"{nameof(OrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                        Verbosity = VerbosityLevel.DEBUG
                    });

                return OrchestrationRuntimeStatus.Completed;
            }

            catch (Exception ex)
            {
                if (syncJob == null)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = "SyncJob is null. Removing the message from the queue..." });
                    return OrchestrationRuntimeStatus.Failed;
                }

                if (syncJob != null)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                    {
                        Message = $"Caught unexpected exception, marking sync job as errored. Exception:\n{ex}",
                        RunId = syncJob.RunId.GetValueOrDefault(Guid.Empty)
                    });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(syncJob.Id,
                                                                    SyncStatus.Error, syncJob.ThresholdViolations, syncJob.RunId.GetValueOrDefault(new Guid())));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });

                    TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");
                }

                throw;
            }
            finally
            {
                if (syncJob?.RunId.HasValue ?? false)
                    _loggingRepository.RemoveSyncJobProperties(syncJob.RunId.Value);
            }
        }

        private void TrackSyncCompleteEvent(IDurableOrchestrationContext context, SyncJob syncJob, SyncCompleteCustomEvent syncCompleteEvent, string successStatus)
        {
            var timeElapsedForJob = (context.CurrentUtcDateTime - syncJob.Timestamp.GetValueOrDefault()).TotalSeconds;
            _telemetryClient.TrackMetric(nameof(Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob);

            syncCompleteEvent.SyncJobTimeElapsedSeconds = timeElapsedForJob.ToString();
            syncCompleteEvent.Result = successStatus;

            var syncCompleteDict = syncCompleteEvent.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(syncCompleteEvent, null));

            _telemetryClient.TrackEvent(nameof(Metric.SyncComplete), syncCompleteDict);
        }

        private TeamsChannelUpdaterSubOrchestratorRequest CreateTeamsGroupUpdaterRequest(bool isInitialSync, Guid runId, ICollection<AzureADTeamsUser> members, AzureADTeamsChannel teamsChannelInfo, RequestType type)
        {
            return new TeamsChannelUpdaterSubOrchestratorRequest
            {
                IsInitialSync = isInitialSync,
                Type = type,
                Members = members,
                TeamsChannelInfo = teamsChannelInfo,
                RunId = runId
            };
        }

        private JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(Guid syncJobId, SyncStatus syncStatus, int thresholdViolations, Guid runId)
        {
            return new JobStatusUpdaterRequest
            {
                RunId = runId,
                JobId = syncJobId,
                Status = syncStatus,
                ThresholdViolations = thresholdViolations
            };
        }

        private string GetUsersDataMessage(Guid targetGroupId, int membersToAdd, int membersToRemove)
        {
            return $"Synchronization for {targetGroupId} is now complete. " +
                   $"{membersToAdd} users have been added. " +
                   $"{membersToRemove} users have been removed.";
        }
    }
}