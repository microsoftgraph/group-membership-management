// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.Entities;
using GraphUpdater.Helpers;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Identity.Client;
using Models;
using Models.Helpers;
using Models.Notifications;
using Models.ServiceBus;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class OrchestratorFunction
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly IGraphUpdaterService _graphUpdaterService = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly IGMMResources _gmmResources = null;
        private readonly IDeltaCachingConfig _deltaCachingConfig = null;

        enum Metric
        {
            SyncComplete,
            SyncJobTimeElapsedSeconds
        }

        public OrchestratorFunction(
            TelemetryClient telemetryClient,
            IGraphUpdaterService graphUpdaterService,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources,
            IDeltaCachingConfig deltaCachingConfig)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _deltaCachingConfig = deltaCachingConfig ?? throw new ArgumentNullException(nameof(deltaCachingConfig));
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task<OrchestrationRuntimeStatus> RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            GroupMembership groupMembership = null;
            MembershipHttpRequest graphRequest = null;
            SyncJob syncJob = null;
            var sourceUsersNotFound = new List<AzureADUser>();
            var destinationUsersNotFound = new List<AzureADUser>();
            var syncCompleteEvent = new SyncCompleteCustomEvent();
            var logger = context.CreateReplaySafeLogger("GraphUpdater.OrchestratorFunction");

            graphRequest = context.GetInput<MembershipHttpRequest>();
            using var scope = logger.BeginSyncJobScope(graphRequest.SyncJob);

            try
            {
                syncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                                       new JobReaderRequest
                                                       {
                                                           SyncJob = graphRequest.SyncJob
                                                       });

                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), new GetGroupRequest { SyncJob = syncJob });
                if (groupId.Equals(Guid.Empty))
                {
                    logger.UnableToGetGroupId(syncJob.Id);
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(syncJob, SyncStatus.Error, syncJob.ThresholdViolations));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob });
                    return OrchestrationRuntimeStatus.Failed;
                }
                logger.GroupIdRetrieved(syncJob.Id, groupId);

                var sourceTypeCounts = JsonParser.GetQueryTypes(syncJob.Query);
                var destination = JsonParser.GetDestination(syncJob);

                syncCompleteEvent.Type = destination.Type.ToString();
                syncCompleteEvent.SourceTypesCounts = sourceTypeCounts;
                syncCompleteEvent.Destination = $"[{{\"type\":\"{destination.Type}\",\"value\":{{\"objectId\":\"{groupId}\"}}}}]";
                syncCompleteEvent.GroupId = groupId.ToString();
                syncCompleteEvent.RunId = syncJob.RunId.ToString();
                syncCompleteEvent.IsDryRunEnabled = false.ToString();
                syncCompleteEvent.ProjectedMemberCount = graphRequest.ProjectedMemberCount.ToString();

                var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                                            new FileDownloaderRequest
                                                                            {
                                                                                FilePath = graphRequest.FilePath,
                                                                                SyncJob = syncJob
                                                                            });

                var membershipJson = TryDecompress(fileContent);
                groupMembership = JsonSerializer.Deserialize<GroupMembership>(membershipJson);

                logger.FunctionStarted(nameof(OrchestratorFunction));
                logger.ReceivedMembership(groupMembership.SourceMembers.Distinct().Count());

                var isValidGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction),
                                           new GroupValidatorRequest
                                           {
                                               SyncJob = syncJob,
                                               GroupId = groupMembership.Destination.ObjectId
                                           });

                if (!isValidGroup)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(syncJob,
                                                                    SyncStatus.DestinationGroupNotFound, syncJob.ThresholdViolations));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationGroupNotFound, ResultStatus = ResultStatus.Success, SyncJob = syncJob });
                    logger.OrchestratorDidNotComplete();

                    return OrchestrationRuntimeStatus.Completed;
                }

                var isInitialSync = syncJob.LastRunTime == SqlDateTime.MinValue.Value;
                syncCompleteEvent.IsInitialSync = isInitialSync.ToString();
                var membersToAdd = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Add).Distinct().ToList();
                syncCompleteEvent.MembersToAdd = membersToAdd.Count.ToString();
                var membersToRemove = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Remove).Distinct().ToList();
                syncCompleteEvent.MembersToRemove = membersToRemove.Count.ToString();

                var membersAddedResponse = await context.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(nameof(GroupUpdaterSubOrchestratorFunction),
                                CreateGroupUpdaterRequest(syncJob, membersToAdd, RequestType.Add, isInitialSync));
                syncCompleteEvent.MembersAdded = membersAddedResponse.SuccessCount.ToString();
                sourceUsersNotFound = membersAddedResponse.UsersNotFound;
                syncCompleteEvent.MembersToAddNotFound = sourceUsersNotFound.Count.ToString();
                syncCompleteEvent.MembersToAddAlreadyExist = membersAddedResponse.UsersAlreadyExist.Count.ToString();

                var membersRemovedResponse = await context.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(nameof(GroupUpdaterSubOrchestratorFunction),
                                CreateGroupUpdaterRequest(syncJob, membersToRemove, RequestType.Remove, isInitialSync));
                syncCompleteEvent.MembersRemoved = membersRemovedResponse.SuccessCount.ToString();
                destinationUsersNotFound = membersRemovedResponse.UsersNotFound;
                syncCompleteEvent.MembersToRemoveNotFound = destinationUsersNotFound.Count.ToString();

                if (membersAddedResponse.Status == GraphUpdaterStatus.GuestError)
                {
                    logger.GuestUsersCannotBeAdded();

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(syncJob,
                                                                        SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup, syncJob.ThresholdViolations));

                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest {
						JobStatus = SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup,
						ResultStatus = ResultStatus.Success,
						SyncJob = syncJob
					});

                    var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                    new GroupNameReaderRequest { SyncJob = syncJob, GroupId = groupMembership.Destination.ObjectId });

                    var additionalContent = new[]
                    {
                                groupMembership.Destination.ObjectId.ToString(),
                                groupName,
                                membersAddedResponse.SuccessCount.ToString(),
                                membersRemovedResponse.SuccessCount.ToString(),
                                DisabledNotificationType.StatusDescriptions[NotificationMessageType.GuestUserFailureNotification]
                    };

                    await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                    new EmailSenderRequest
                                                    {
                                                        SyncJob = syncJob,
                                                        NotificationType = NotificationMessageType.GuestUserFailureNotification,
                                                        AdditionalContentParams = additionalContent
                                                    });

                    TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");

                    logger.FunctionCompleted(nameof(OrchestratorFunction));

                    return OrchestrationRuntimeStatus.Completed;
                }

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
                                                        AdditionalContentParams = additionalContent
                                                    });
                }


                var message = GetUsersDataMessage(groupMembership.Destination.ObjectId, membersToAdd.Count, membersToRemove.Count);
                logger.UsersDataInfo(message);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(syncJob,
                                                                    SyncStatus.Idle, 0, membersAddedResponse.SuccessCount, membersRemovedResponse.SuccessCount));
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, SyncJob = syncJob });
                if (!context.IsReplaying)
                {
                    if (membersAddedResponse.SuccessCount + membersAddedResponse.UsersNotFound.Count + membersAddedResponse.UsersAlreadyExist.Count == membersToAdd.Count &&
                        membersRemovedResponse.SuccessCount + membersRemovedResponse.UsersNotFound.Count == membersToRemove.Count)
                    {
                        TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Success");
                    }
                    else
                    {
                        TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "PartialSuccess");
                    }
                }

                if (_deltaCachingConfig.DeltaCacheEnabled) await UpdateCachesAsync(context, sourceUsersNotFound, destinationUsersNotFound, syncJob, groupMembership.SourceMembers);

                logger.FunctionCompleted(nameof(OrchestratorFunction));

                return OrchestrationRuntimeStatus.Completed;
            }
            catch (HttpRequestException httpEx)
            {
                logger.OrchestratorHttpException(httpEx);
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(syncJob, SyncStatus.TransientError, syncJob.ThresholdViolations));
                throw;
            }
            catch (MsalClientException msalEx)
            {
                if (msalEx.ErrorCode == "MULTIPLE_MATCHING_TOKENS_DETECTED")
                {
                    logger.OrchestratorMsalException(msalEx);
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(syncJob, SyncStatus.TransientError, syncJob.ThresholdViolations));
                }
                throw;
            }
            catch (Exception ex)
            {
                logger.OrchestratorUnexpectedException(ex);

                if (syncJob == null)
                {
                    logger.SyncJobIsNull();
                    return OrchestrationRuntimeStatus.Failed;
                }

                if (syncJob != null && groupMembership != null && groupMembership.SyncJobId != Guid.Empty)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(syncJob,
                                                                    SyncStatus.Error, syncJob.ThresholdViolations));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob });
                }

                TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");

                throw;
            }
            finally
            {
            }
        }

        public async Task UpdateCachesAsync(TaskOrchestrationContext context,
                                                List<AzureADUser> sourceUsersNotFound,
                                                List<AzureADUser> destinationUsersNotFound,
                                                SyncJob syncJob,
                                                List<AzureADUser> sourceMembers)
        {
            if (sourceUsersNotFound != null && destinationUsersNotFound != null)
            {
                var destination = JsonParser.GetDestination(syncJob);

                var sourceObjectIds = new HashSet<Guid>(sourceUsersNotFound.Select(emp => emp.ObjectId));
                var destinationObjectIds = new HashSet<Guid>(destinationUsersNotFound.Select(emp => emp.ObjectId));

                // Compute count by unioning into a copy to avoid allocating intermediate LINQ collection
                var totalUsersNotFoundCount = sourceObjectIds.Count + destinationObjectIds.Count(id => !sourceObjectIds.Contains(id));

                if (!context.IsReplaying && totalUsersNotFoundCount > 0) { TrackUsersNotFoundEvent(syncJob.RunId, totalUsersNotFoundCount, destination.ObjectId); }
                var sourceGroups = new Dictionary<Guid, HashSet<Guid>>();
                var destinationUserIds = new HashSet<Guid>();

                foreach (var member in sourceMembers)
                {
                    if (sourceObjectIds.Contains(member.ObjectId) && member.SourceGroups != null)
                    {
                        foreach (var sourceGroupId in member.SourceGroups)
                        {
                            if (sourceGroupId == Guid.Empty)
                            {
                                continue;
                            }

                            if (!sourceGroups.TryGetValue(sourceGroupId, out var userIdsForGroup))
                            {
                                userIdsForGroup = new HashSet<Guid>();
                                sourceGroups[sourceGroupId] = userIdsForGroup;
                            }

                            userIdsForGroup.Add(member.ObjectId);
                        }
                    }

                    if (destinationObjectIds.Contains(member.ObjectId))
                    {
                        destinationUserIds.Add(member.ObjectId);
                    }
                }

                if (sourceGroups.Count > 0)
                {
                    // These calls to the cache updater suborchestrator were once done in parallel, but this caused an OutOfMemoryException due to loading multiple big files at once into memory.
                    // Although this does not affect many sync runs, we should revise it once we have upgraded our service plan.
                    foreach (var sourceGroup in sourceGroups)
                    {
                        await context.CallSubOrchestratorAsync(nameof(CacheUserUpdaterSubOrchestratorFunction),
                            new CacheUserUpdaterRequest
                            {
                                GroupId = sourceGroup.Key,
                                UserIds = sourceGroup.Value,
                                SyncJob = syncJob
                            });
                    }
                }

                if (destinationUserIds.Count > 0)
                {
                    await context.CallSubOrchestratorAsync(
                        nameof(CacheUserUpdaterSubOrchestratorFunction),
                        new CacheUserUpdaterRequest
                        {
                            GroupId = destination.ObjectId,
                            UserIds = destinationUserIds,
                            SyncJob = syncJob
                        });
                }
            }
        }

        private void TrackUsersNotFoundEvent(Guid? runId, int usersNotFoundCount, Guid groupId)
        {
            var usersNotFoundEvent = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() },
                { "TargetGroupId", groupId.ToString() },
                { "UsersNotFound", usersNotFoundCount.ToString() }
            };
            _telemetryClient.TrackEvent("UsersNotFoundCount", usersNotFoundEvent);
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

        private static JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(SyncJob syncJob, SyncStatus syncStatus, int thresholdViolations, int usersAdded = 0, int usersRemoved = 0)
        {
            return new JobStatusUpdaterRequest
            {
                SyncJob = syncJob,
                Status = syncStatus,
                ThresholdViolations = thresholdViolations,
                UsersAdded = usersAdded,
                UsersRemoved = usersRemoved
            };
        }

        private GroupUpdaterRequest CreateGroupUpdaterRequest(SyncJob syncJob, ICollection<AzureADUser> members, RequestType type, bool isInitialSync)
        {
            return new GroupUpdaterRequest
            {
                SyncJob = syncJob,
                Members = members,
                Type = type,
                IsInitialSync = isInitialSync
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

        private string GetUsersDataMessage(Guid targetGroupId, int membersToAdd, int membersToRemove)
        {
            return $"Synchronization for {targetGroupId} is now complete. " +
                   $"{membersToAdd} users have been added. " +
                   $"{membersToRemove} users have been removed.";
        }
    }
}
