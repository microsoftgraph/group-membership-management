// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
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
using Repositories.Contracts.Helpers;
using Models.Entities;
using System.Text.Json;
using Services.TeamsChannelUpdater.Contracts;
using Models.Notifications;
using Models.Helpers;

namespace Hosts.TeamsChannelUpdater
{
    public class OrchestratorFunction
    {
        private const string SyncCompletedEmailBody = "SyncCompletedEmailBody";
        private readonly TelemetryClient _telemetryClient;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly IGMMResources _gmmResources = null;
        enum Metric
        {
            SyncComplete,
            SyncJobTimeElapsedSeconds
        }

        public OrchestratorFunction(TelemetryClient telemetryClient,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            TeamsGroupMembership groupMembership = null;
            MembershipHttpRequest graphRequest = null;
            SyncJob syncJob = null;
            var syncCompleteEvent = new SyncCompleteCustomEvent();

            graphRequest = context.GetInput<MembershipHttpRequest>();

            var logger = context.CreateReplaySafeLogger("TeamsChannelUpdater.OrchestratorFunction");
            using var scope = logger.BeginSyncJobScope(graphRequest.SyncJob);

            try
            {
                syncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                                       new JobReaderRequest
                                                       {
                                                           JobId = graphRequest.SyncJob.Id,
                                                           SyncJob = graphRequest.SyncJob
                                                       });

                logger.FunctionStarted(nameof(OrchestratorFunction));

                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), syncJob);
                var channelId = await context.CallActivityAsync<string>(nameof(GetChannelFunction), syncJob);

                var sourceTypesCounts = JsonParser.GetQueryTypes(syncJob.Query);
                var destination = JsonParser.GetDestination(syncJob);

                syncCompleteEvent.Type = syncJob.MembershipType;
                syncCompleteEvent.Destination = $"[{{\"type\":\"{syncJob.MembershipType}\",\"value\":{{\"objectId\":\"{groupId}\",\"channelId\":\"{channelId}\"}}}}]";
                syncCompleteEvent.GroupId = groupId.ToString();
                syncCompleteEvent.ChannelId = channelId;
                syncCompleteEvent.SourceTypesCounts = sourceTypesCounts;
                syncCompleteEvent.RunId = syncJob.RunId.ToString();
                syncCompleteEvent.IsDryRunEnabled = false.ToString();
                syncCompleteEvent.ProjectedMemberCount = graphRequest.ProjectedMemberCount.ToString();

                var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                                            new FileDownloaderRequest
                                                                            {
                                                                                FilePath = graphRequest.FilePath,
                                                                                SyncJob = syncJob
                                                                            });

                JsonSerializerOptions options = new JsonSerializerOptions();
                options.Converters.Add(new AzureADTeamsUserConverter());
                var decompressedContent = TryDecompress(fileContent);
                groupMembership = JsonSerializer.Deserialize<TeamsGroupMembership>(decompressedContent, options);
                if (groupMembership == null)
                {
                    throw new InvalidOperationException("Deserialized group membership is null.");
                }

                logger.ReceivedMembership(groupMembership.SourceMembers.Distinct().Count());

                var isInitialSync = syncJob.LastRunTime == DateTime.FromFileTimeUtc(0);
                syncCompleteEvent.IsInitialSync = isInitialSync.ToString();
                var membersToAdd = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Add).Distinct().ToList();
                syncCompleteEvent.MembersToAdd = membersToAdd.Count.ToString();
                var membersToRemove = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Remove).Distinct().ToList();
                syncCompleteEvent.MembersToRemove = membersToRemove.Count.ToString();


                var membersAddedResponse = await context.CallSubOrchestratorAsync<TeamsChannelUpdaterSubOrchestratorResponse>(nameof(TeamsChannelUpdaterSubOrchestratorFunction),
                                CreateTeamsGroupUpdaterRequest(isInitialSync,
                                syncJob,
                                membersToAdd,
                                destination,
                                RequestType.Add));
                syncCompleteEvent.MembersAdded = membersAddedResponse.SuccessCount.ToString();
                syncCompleteEvent.MembersToAddNotFound = membersAddedResponse.UsersNotFound.Count.ToString();

                var membersRemovedResponse = await context.CallSubOrchestratorAsync<TeamsChannelUpdaterSubOrchestratorResponse>(nameof(TeamsChannelUpdaterSubOrchestratorFunction),
                                CreateTeamsGroupUpdaterRequest(isInitialSync,
                                syncJob,
                                membersToRemove,
                                destination,
                                RequestType.Remove));
                syncCompleteEvent.MembersRemoved = membersRemovedResponse.SuccessCount.ToString();
                syncCompleteEvent.MembersToRemoveNotFound = membersRemovedResponse.UsersNotFound.Count.ToString();

                if (isInitialSync)
                {
                    var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                    new GroupNameReaderRequest { SyncJob = syncJob, GroupId = groupMembership.Destination.ObjectId });

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
                            SyncJob = syncJob,
                            NotificationType = NotificationMessageType.SyncCompletedNotification,
                            AdditionalContentParams = additionalContent,
                        });
                }


                logger.SyncComplete(groupMembership.Destination.ObjectId, membersToAdd.Count, membersToRemove.Count);

                if (membersAddedResponse.UsersFailed.Count > 0)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                        SyncStatus.TeamsChannelError, 0, syncJob));
                }
                else
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                        SyncStatus.Idle, 0, syncJob));
                }

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, SyncJob = syncJob });
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

                logger.OrchestratorCompleted(nameof(OrchestratorFunction), context.CurrentUtcDateTime);
            }

            catch (Exception ex)
            {
                if (syncJob == null)
                {
                    logger.SyncJobIsNull();
                    return;
                }

                logger.UnexpectedExceptionCaught(ex);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                CreateJobStatusUpdaterRequest(syncJob.Id,
                                                                SyncStatus.Error, syncJob.ThresholdViolations, syncJob));
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob });

                if (!context.IsReplaying)
                {
                    TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");
                }

                throw;
            }
        }

        private void TrackSyncCompleteEvent(TaskOrchestrationContext context, SyncJob syncJob, SyncCompleteCustomEvent syncCompleteEvent, string successStatus)
        {
            var timeElapsedForJob = (context.CurrentUtcDateTime - syncJob.LastSuccessfulStartTime).TotalSeconds;
            _telemetryClient.TrackMetric(nameof(Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob);

            syncCompleteEvent.SyncJobTimeElapsedSeconds = timeElapsedForJob.ToString();
            syncCompleteEvent.Result = successStatus;

            var syncCompleteDict = syncCompleteEvent.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(syncCompleteEvent, null));

            _telemetryClient.TrackEvent(nameof(Metric.SyncComplete), syncCompleteDict);
        }

        private TeamsChannelUpdaterSubOrchestratorRequest CreateTeamsGroupUpdaterRequest(bool isInitialSync, SyncJob syncJob, ICollection<AzureADTeamsUser> members, AzureADTeamsChannel teamsChannelInfo, RequestType type)
        {
            return new TeamsChannelUpdaterSubOrchestratorRequest
            {
                IsInitialSync = isInitialSync,
                Type = type,
                Members = members,
                TeamsChannelInfo = teamsChannelInfo,
                SyncJob = syncJob
            };
        }

        private JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(Guid syncJobId, SyncStatus syncStatus, int thresholdViolations, SyncJob syncJob)
        {
            return new JobStatusUpdaterRequest
            {
                SyncJob = syncJob,
                JobId = syncJobId,
                Status = syncStatus,
                ThresholdViolations = thresholdViolations
            };
        }

        private static string TryDecompress(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            try
            {
                return TextCompressor.Decompress(content);
            }
            catch (FormatException)
            {
                return content;
            }
        }
    }
}

