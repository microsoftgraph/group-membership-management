// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Activity.EmailSender;
using MembershipAggregator.Helpers;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.Helpers;
using Models.Notifications;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorFunction
    {
        private const int MEMBERS_LIMIT = 100000;
        private readonly IThresholdConfig _thresholdConfig = null;
        private readonly IGraphAPIService _graphAPIService = null;
        private readonly TelemetryClient _telemetryClient = null;

        public MembershipSubOrchestratorFunction(IThresholdConfig thresholdConfig, IGraphAPIService graphAPIService, TelemetryClient telemetryClient)
        {
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(MembershipSubOrchestratorFunction))]
        public async Task<MembershipSubOrchestratorResponse> RunMembershipSubOrchestratorFunctionAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<MembershipSubOrchestratorRequest>();
            var runId = request.SyncJob.RunId ?? Guid.Empty;
            var proxy = context.CreateEntityProxy<IJobTracker>(request.EntityId);
            var state = await proxy.GetState();
            var downloadFileTasks = new List<Task<(string FilePath, string Content)>>();

            foreach (var part in state.CompletedParts)
            {
                var downloadRequest = new FileDownloaderRequest { FilePath = part, SyncJob = request.SyncJob };
                downloadFileTasks.Add(context.CallActivityAsync<(string FilePath, string Content)>(nameof(FileDownloaderFunction), downloadRequest));
            }

            var completedDownloadTasks = await Task.WhenAll(downloadFileTasks);
            var (SourceMembership, DestinationMembership) = ExtractMembershipInformationAsync(completedDownloadTasks, state.DestinationPart);
            DeltaCalculatorRequest deltaCalculatorRequest;

            if (SourceMembership == null || DestinationMembership == null)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.Error,
                                                    IsDryRun = false,
                                                    IncrementThresholdViolations = false,
                                                    DeltaStatus = MembershipDeltaStatus.Error
                                                });

                if (SourceMembership == null)
                {
                    await LogMessageAsync(context, $"SourceMembership is missing for TargetOfficeGroupId {request.GroupId}. Marking job as 'Error'.", runId);
                }
                else
                {
                    await LogMessageAsync(context, $"DestinationMembership is missing for TargetOfficeGroupId {request.GroupId}. Marking job as 'Error'.", runId);
                }

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    JobStatus = SyncStatus.Error,
                    ResultStatus = ResultStatus.Failure,
                    RunId = runId
                });

                return new MembershipSubOrchestratorResponse
                {
                    MembershipDeltaStatus = MembershipDeltaStatus.Error
                };
            }

            if (!request.SyncJob.AllowEmptyDestination && SourceMembership.SourceMembers.Count == 0)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.MembershipDataNotFound,
                                                    IsDryRun = false,
                                                    IncrementThresholdViolations = false,
                                                    DeltaStatus = MembershipDeltaStatus.Error
                                                });
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"Sources are empty for TargetOfficeGroupId {request.GroupId}. Empty destination is not allowed for this group. Marking job as 'MembershipDataNotFound'.",
                            RunId = runId
                        },
                        Verbosity = VerbosityLevel.INFO
                    });

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    JobStatus = SyncStatus.MembershipDataNotFound,
                    ResultStatus = ResultStatus.Success,
                    RunId = runId
                });

                var groupInformation = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), new GroupNameReaderRequest
                                                                                    {
                                                                                        SyncJob = request.SyncJob,
                                                                                        GroupId = request.GroupId
                                                                                    });
                await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                new EmailSenderRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    NotificationType = NotificationMessageType.NoDataNotification,
                                                    AdditionalContentParams = new[] { request.GroupId.ToString(), groupInformation.Name },
                                                });

                return new MembershipSubOrchestratorResponse
                {
                    MembershipDeltaStatus = MembershipDeltaStatus.Error

                };
            }

            if (SourceMembership.SourceMembers.Count >= MEMBERS_LIMIT || DestinationMembership.SourceMembers.Count >= MEMBERS_LIMIT)
            {
                var sourceFilePath = GenerateFileName(request.SyncJob, request.GroupId, "SourceMembership", context);
                var sourceContent = TextCompressor.Compress(JsonSerializer.Serialize(SourceMembership));
                var sourceRequest = new FileUploaderRequest { FilePath = sourceFilePath, Content = sourceContent, SyncJob = request.SyncJob };

                var destinationFilePath = GenerateFileName(request.SyncJob, request.GroupId, "DestinationMembership", context);
                var destinationContent = TextCompressor.Compress(JsonSerializer.Serialize(DestinationMembership));
                var destinationRequest = new FileUploaderRequest { FilePath = destinationFilePath, Content = destinationContent, SyncJob = request.SyncJob };

                await Task.WhenAll
                (
                    context.CallActivityAsync(nameof(FileUploaderFunction), sourceRequest),
                    context.CallActivityAsync(nameof(FileUploaderFunction), destinationRequest)
                );

                deltaCalculatorRequest = new DeltaCalculatorRequest
                {
                    RunId = runId,
                    SourceGroupMembership = string.Empty,
                    DestinationGroupMembership = string.Empty,
                    ReadFromBlobs = true,
                    SourceMembershipFilePath = sourceFilePath,
                    DestinationMembershipFilePath = destinationFilePath
                };

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                {
                    Message = new LogMessage
                    {
                        Message = $"Reading from blobs, SourceMembershipFilePath: {deltaCalculatorRequest.SourceMembershipFilePath}, DestinationMembershipFilePath: {deltaCalculatorRequest.DestinationMembershipFilePath}",
                        RunId = runId
                    },
                    Verbosity = VerbosityLevel.INFO
                });
            }
            else
            {
                deltaCalculatorRequest = new DeltaCalculatorRequest
                {
                    RunId = runId,
                    SourceGroupMembership = TextCompressor.Compress(JsonSerializer.Serialize(SourceMembership)),
                    DestinationGroupMembership = TextCompressor.Compress(JsonSerializer.Serialize(DestinationMembership)),
                    ReadFromBlobs = false,
                    SourceMembershipFilePath = string.Empty,
                    DestinationMembershipFilePath = string.Empty
                };
            }

            var deltaResponse = await context.CallActivityAsync<DeltaCalculatorResponse>(nameof(DeltaCalculatorFunction), deltaCalculatorRequest);

            if (deltaResponse != null && deltaCalculatorRequest.ReadFromBlobs && deltaCalculatorRequest.SourceMembershipFilePath != null && deltaCalculatorRequest.DestinationMembershipFilePath != null)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                {
                    Message = new LogMessage
                    {
                        Message = $"deltaResponse: {deltaResponse.MembershipDeltaStatus}, SourceMembershipFilePath: {deltaCalculatorRequest.SourceMembershipFilePath}, DestinationMembershipFilePath: {deltaCalculatorRequest.DestinationMembershipFilePath}",
                        RunId = runId
                    },
                    Verbosity = VerbosityLevel.INFO
                });
                await context.CallActivityAsync(nameof(FileDeleterFunction), new FileDeleterRequest { FilePath = deltaCalculatorRequest.SourceMembershipFilePath, RunId = runId });
                await context.CallActivityAsync(nameof(FileDeleterFunction), new FileDeleterRequest { FilePath = deltaCalculatorRequest.DestinationMembershipFilePath, RunId = runId });
            }

            if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
            {
                var uploadRequest = CreateAggregatedFileUploaderRequest(SourceMembership, deltaResponse, request.SyncJob, request.GroupId, context);
                await context.CallActivityAsync(nameof(FileUploaderFunction), uploadRequest);
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"Uploaded membership file {uploadRequest.FilePath} with {SourceMembership.SourceMembers.Count} unique members",
                            RunId = runId
                        },
                        Verbosity = VerbosityLevel.INFO
                    });

                return new MembershipSubOrchestratorResponse
                {
                    FilePath = uploadRequest.FilePath,
                    MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus,
                    ProjectedMemberCount = SourceMembership.SourceMembers.Count,
                    MembersToBeAdded = deltaResponse.MembersToAddCount,
                    MembersToBeRemoved = deltaResponse.MembersToRemoveCount,
                };
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.ThresholdExceeded)
            {
                var uploadRequest = CreateAggregatedFileUploaderRequest(SourceMembership, deltaResponse, request.SyncJob, request.GroupId, context);
                await context.CallActivityAsync(nameof(FileUploaderFunction), uploadRequest);
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"Uploaded membership file {uploadRequest.FilePath} with {SourceMembership.SourceMembers.Count} unique members",
                            RunId = runId
                        },
                        Verbosity = VerbosityLevel.INFO
                    });

                var currentThresholdViolations = request.SyncJob.ThresholdViolations + 1;
                SyncStatus status = currentThresholdViolations >= _thresholdConfig.NumberOfThresholdViolationsToDisableJob
                                    ? SyncStatus.ThresholdExceeded
                                    : SyncStatus.Idle;

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = status,
                                                    IsDryRun = false,
                                                    IncrementThresholdViolations = true,
                                                    DeltaStatus = MembershipDeltaStatus.ThresholdExceeded
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = status, ResultStatus = ResultStatus.Success, RunId = runId });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.DryRun)
            {
                var message = $"A Dry Run Synchronization for {request.GroupId} is now complete. " +
                              $"{deltaResponse.MembersToAddCount} users would have been added. " +
                              $"{deltaResponse.MembersToRemoveCount} users would have been removed.";

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage { Message = message, RunId = runId },
                        Verbosity = VerbosityLevel.INFO
                    });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.Idle,
                                                    IsDryRun = true,
                                                    IncrementThresholdViolations = false,
                                                    DeltaStatus = MembershipDeltaStatus.DryRun
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = runId });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Error)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.Error,
                                                    IsDryRun = false,
                                                    IncrementThresholdViolations = false,
                                                    DeltaStatus = MembershipDeltaStatus.Error
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.NoChanges)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = new LogMessage
                    {
                        Message = $"There are no membership changes for TargetOfficeGroupId {request.GroupId}.",
                        RunId = runId
                    },
                    Verbosity = VerbosityLevel.INFO
                });

                var sourceTypeCounts = JsonParser.GetQueryTypes(request.SyncJob.Query);
                var channelId = await context.CallActivityAsync<string>(nameof(GetChannelFunction), request.SyncJob);
                var syncCompleteEvent = new SyncCompleteCustomEvent
                {
                    Type = request.SyncJob.MembershipType,
                    SourceTypesCounts = sourceTypeCounts,
                    Destination = $"[{{\"type\":\"{request.SyncJob.MembershipType}\",\"value\":{{\"objectId\":\"{request.GroupId}\"}}}}]",
                    GroupId = request.GroupId.ToString(),
                    ChannelId = channelId,
                    RunId = runId.ToString(),
                    IsDryRunEnabled = false.ToString(),
                    ProjectedMemberCount = "0",
                    MembersToAdd = "0",
                    MembersToRemove = "0",
                    MembersAdded = "0",
                    MembersRemoved = "0",
                    MembersToAddNotFound = "0",
                    MembersToRemoveNotFound = "0",
                    IsInitialSync = $"{request.SyncJob.LastRunTime == SqlDateTime.MinValue.Value}"
                };

                var dbSyncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                       new JobReaderRequest
                                       {
                                           JobId = request.SyncJob.Id,
                                           RunId = runId
                                       });

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    JobStatus = SyncStatus.Idle,
                    ResultStatus = ResultStatus.Success,
                    RunId = runId
                });

                if (!context.IsReplaying)
                    TrackSyncCompleteEvent(context, dbSyncJob, syncCompleteEvent, "Success");

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = request.SyncJob,
                                    Status = SyncStatus.Idle,
                                    IsDryRun = false,
                                    IncrementThresholdViolations = false,
                                    DeltaStatus = MembershipDeltaStatus.NoChanges
                                });
            }

            return new MembershipSubOrchestratorResponse
            {
                MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus,
                MembersToBeAdded = deltaResponse.MembersToAddCount,
                MembersToBeRemoved = deltaResponse.MembersToRemoveCount
            };
        }

        private async Task LogMessageAsync(IDurableOrchestrationContext context, string message, Guid runId)
        {
            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = new LogMessage
                    {
                        Message = message,
                        RunId = runId
                    },
                    Verbosity = VerbosityLevel.INFO
                });
        }

        private (GroupMembership SourceMembership, GroupMembership DestinationMembership)
                ExtractMembershipInformationAsync((string FilePath, string Content)[] allGroupMemberships, string destinationPath)
        {
            var sourceGroupsMemberships = allGroupMemberships
                                            .Where(x => x.FilePath != destinationPath)
                                            .Select(x => JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(x.Content)))
                                            .ToList();

            var sourceGroupMembership = sourceGroupsMemberships[0];
            var toInclude = sourceGroupsMemberships.Where(g => !g.Exclusionary).SelectMany(x => x.SourceMembers).ToList();
            var toExclude = sourceGroupsMemberships.Where(g => g.Exclusionary).SelectMany(x => x.SourceMembers).ToList();
            var diff = toInclude.Except(toExclude).ToList();

            var source = sourceGroupsMemberships.SelectMany(x => x.SourceMembers).ToList();
            var listGrouped = source.GroupBy(u => u.ObjectId)
                               .Select(u => new AzureADUser() { ObjectId = u.Key, SourceGroups = u.Select(y => y.SourceGroup).Distinct().ToList() })
                               .ToList();

            var objectIds = new HashSet<Guid>(diff.Select(u => u.ObjectId));
            var sourceMembers = listGrouped.Where(u => objectIds.Contains(u.ObjectId)).ToList();

            sourceGroupMembership.SourceMembers = sourceMembers;

            var destinationMembershipFile = allGroupMemberships.First(x => x.FilePath == destinationPath);
            var destinationGroupMembership = JsonSerializer.Deserialize<GroupMembership>(TextCompressor.Decompress(destinationMembershipFile.Content));

            return (sourceGroupMembership, destinationGroupMembership);
        }

        private FileUploaderRequest CreateAggregatedFileUploaderRequest(GroupMembership membership, DeltaCalculatorResponse deltaResponse, SyncJob syncJob, Guid groupId, IDurableOrchestrationContext context)
        {
            var membersToAdd = JsonSerializer.Deserialize<ICollection<AzureADUser>>(TextCompressor.Decompress(deltaResponse.CompressedMembersToAddJSON));
            var membersToRemove = JsonSerializer.Deserialize<ICollection<AzureADUser>>(TextCompressor.Decompress(deltaResponse.CompressedMembersToRemoveJSON));

            var newMembership = (GroupMembership)membership.Clone();
            newMembership.SourceMembers.Clear();
            newMembership.SourceMembers.AddRange(membersToAdd);
            newMembership.SourceMembers.AddRange(membersToRemove);

            var serializerSettings = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
            };

            var filePath = GenerateFileName(syncJob, groupId, "Aggregated", context);
            var content = TextCompressor.Compress(JsonSerializer.Serialize(newMembership, serializerSettings));

            return new FileUploaderRequest { FilePath = filePath, Content = content, SyncJob = syncJob };
        }

        private string GenerateFileName(SyncJob syncJob, Guid groupId, string suffix, IDurableOrchestrationContext context)
        {
            var timeStamp = context.CurrentUtcDateTime.ToString("MMddyyyy-HHmm");
            return $"/{groupId}/{timeStamp}_{syncJob.RunId}_{suffix}.json";
        }

        private void TrackSyncCompleteEvent(IDurableOrchestrationContext context, SyncJob syncJob, SyncCompleteCustomEvent syncCompleteEvent, string successStatus)
        {
            var timeElapsedForJob = (context.CurrentUtcDateTime - syncJob.LastSuccessfulStartTime).TotalSeconds;
            _telemetryClient.TrackMetric(nameof(Services.Entities.Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob);

            syncCompleteEvent.SyncJobTimeElapsedSeconds = timeElapsedForJob.ToString();
            syncCompleteEvent.Result = successStatus;

            var syncCompleteDict = syncCompleteEvent.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(syncCompleteEvent, null));

            _telemetryClient.TrackEvent(nameof(Services.Entities.Metric.SyncComplete), syncCompleteDict);
        }
    }
}
