// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using MembershipAggregator.Activity.EmailSender;
using MembershipAggregator.Helpers;
using MembershipAggregator.Services.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Models.Notifications;
using Models.ServiceBus;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorFunction
    {
        private readonly IGraphAPIService _graphAPIService = null;
        private readonly TelemetryClient _telemetryClient = null;
        private readonly MultiLaneConfig _multilaneConfig = null;

        public MembershipSubOrchestratorFunction(IGraphAPIService graphAPIService, TelemetryClient telemetryClient, MultiLaneConfig multilaneConfig)
        {
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _multilaneConfig = multilaneConfig ?? throw new ArgumentNullException(nameof(multilaneConfig));
        }

        [Function(nameof(MembershipSubOrchestratorFunction))]
        public async Task<MembershipSubOrchestratorResponse> RunMembershipSubOrchestratorFunctionAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<MembershipSubOrchestratorRequest>();
            var runId = request.SyncJob.RunId ?? Guid.Empty;
            var currentPart = request.CurrentPart;
            var totalParts = request.TotalParts;

            var logger = context.CreateReplaySafeLogger("MembershipAggregator.MembershipSubOrchestratorFunction");
            using var scope = logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = currentPart,
                ["TotalParts"] = totalParts
            });

            var currentUtcDateTime = context.CurrentUtcDateTime;
            var completedParts = request.CompletedParts;
            var destinationPart = request.DestinationPart;
            var expectedPartCount = totalParts;

            MembershipExtractionResponse membershipExtractionResponse;
            if (!HasCompleteMembershipSnapshot(completedParts, destinationPart, expectedPartCount))
            {
                membershipExtractionResponse = new MembershipExtractionResponse
                {
                    IsSuccessful = false,
                    ErrorMessage = $"Membership snapshot is incomplete or invalid. Expected {expectedPartCount} parts and received {completedParts?.Count ?? 0}."
                };
            }
            else
            {
                var membershipExtractionRequest = new MembershipExtractionRequest
                {
                    CompletedParts = completedParts.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList(),
                    DestinationPart = destinationPart,
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    GroupId = request.GroupId,
                    CurrentUtcDateTime = currentUtcDateTime
                };

                membershipExtractionResponse = await context.CallActivityAsync<MembershipExtractionResponse>(nameof(MembershipExtractionFunction), membershipExtractionRequest);
            }

            if (!membershipExtractionResponse.IsSuccessful)
            {
                var extractionError = string.IsNullOrWhiteSpace(membershipExtractionResponse.ErrorMessage)
                                        ? "Unknown membership extraction failure"
                                        : membershipExtractionResponse.ErrorMessage;

                logger.MembershipExtractionFailed(request.GroupId, extractionError);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    Status = SyncStatus.Error,
                                                    IsDryRun = false,
                                                    IsNoOpSync = false
                                                });

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = SyncStatus.Error,
                    ResultStatus = ResultStatus.Failure
                });

                return new MembershipSubOrchestratorResponse
                {
                    MembershipDeltaStatus = MembershipDeltaStatus.Error
                };
            }
            var sourceMembershipFilePath = membershipExtractionResponse.SourceMembershipFilePath;
            var destinationMembershipFilePath = membershipExtractionResponse.DestinationMembershipFilePath;

            if (string.IsNullOrWhiteSpace(sourceMembershipFilePath) || string.IsNullOrWhiteSpace(destinationMembershipFilePath))
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    Status = SyncStatus.Error,
                                                    IsDryRun = false,
                                                    IsNoOpSync = false
                                                });

                var missingComponent = string.IsNullOrWhiteSpace(sourceMembershipFilePath) ? "SourceMembership" : "DestinationMembership";
                logger.MissingMembershipComponent(missingComponent, request.GroupId);

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = SyncStatus.Error,
                    ResultStatus = ResultStatus.Failure
                });

                return new MembershipSubOrchestratorResponse
                {
                    MembershipDeltaStatus = MembershipDeltaStatus.Error
                };
            }

            if (!request.SyncJob.AllowEmptyDestination && membershipExtractionResponse.SourceMemberCount == 0)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    Status = SyncStatus.MembershipDataNotFound,
                                                    IsDryRun = false,
                                                    IsNoOpSync = false
                                                });

                logger.SourcesEmptyForGroup(request.GroupId);

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = SyncStatus.MembershipDataNotFound,
                    ResultStatus = ResultStatus.Success
                });

                var groupInformation = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), new GroupNameReaderRequest
                                                                                    {
                                                                                        SyncJob = request.SyncJob,
                                                                                        CurrentPart = currentPart,
                                                                                        TotalParts = totalParts,
                                                                                        GroupId = request.GroupId
                                                                                    });
                await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                new EmailSenderRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    NotificationType = NotificationMessageType.NoDataNotification,
                                                    AdditionalContentParams = new[]
                                                    {
                                                        request.GroupId.ToString(),
                                                        groupInformation.Name,
                                                        currentUtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                                                    },
                                                });

                return new MembershipSubOrchestratorResponse
                {
                    MembershipDeltaStatus = MembershipDeltaStatus.Error

                };
            }

            var deltaCalculatorRequest = new DeltaCalculatorRequest
            {
                SyncJob = request.SyncJob,
                CurrentPart = currentPart,
                TotalParts = totalParts,
                SourceGroupMembership = string.Empty,
                DestinationGroupMembership = string.Empty,
                ReadFromBlobs = true,
                SourceMembershipFilePath = sourceMembershipFilePath,
                DestinationMembershipFilePath = destinationMembershipFilePath
            };

            logger.ReadingMembershipFromBlobs(sourceMembershipFilePath, destinationMembershipFilePath);

            var deltaResponse = await context.CallActivityAsync<DeltaCalculatorResponse>(nameof(DeltaCalculatorFunction), deltaCalculatorRequest);

            AggregatedMembershipUploadResponse aggregatedMembershipResponse = null;

            if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
            {
                aggregatedMembershipResponse = await context.CallActivityAsync<AggregatedMembershipUploadResponse>(nameof(AggregatedMembershipUploaderFunction),
                    new AggregatedMembershipUploadRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = currentPart,
                        TotalParts = totalParts,
                        GroupId = request.GroupId,
                        SourceMembershipFilePath = sourceMembershipFilePath,
                        CompressedMembersToAddJson = deltaResponse.CompressedMembersToAddJSON,
                        CompressedMembersToRemoveJson = deltaResponse.CompressedMembersToRemoveJSON,
                        CurrentUtcDateTime = currentUtcDateTime
                    });

                if (!aggregatedMembershipResponse.IsSuccessful)
                {
                    var errorMessage = string.IsNullOrWhiteSpace(aggregatedMembershipResponse.ErrorMessage)
                        ? "Aggregated membership upload failed without an error message."
                        : aggregatedMembershipResponse.ErrorMessage;

                    logger.AggregatedUploadError(errorMessage);

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    new JobStatusUpdaterRequest
                                                    {
                                                        SyncJob = request.SyncJob,
                                                        CurrentPart = currentPart,
                                                        TotalParts = totalParts,
                                                        Status = SyncStatus.Error,
                                                        IsDryRun = false,
                                                        IsNoOpSync = false
                                                    });

                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = currentPart,
                        TotalParts = totalParts,
                        JobStatus = SyncStatus.Error,
                        ResultStatus = ResultStatus.Failure
                    });

                    await DeleteMembershipFilesAsync(context, deltaCalculatorRequest);

                    return new MembershipSubOrchestratorResponse
                    {
                        MembershipDeltaStatus = MembershipDeltaStatus.Error
                    };
                }

                logger.UploadedMembershipFile(aggregatedMembershipResponse.FilePath, aggregatedMembershipResponse.MemberCount);

                await DeleteMembershipFilesAsync(context, deltaCalculatorRequest);

                return new MembershipSubOrchestratorResponse
                {
                    FilePath = aggregatedMembershipResponse.FilePath,
                    MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus,
                    ProjectedMemberCount = membershipExtractionResponse.SourceMemberCount,
                    MembersToBeAdded = deltaResponse.MembersToAddCount,
                    MembersToBeRemoved = deltaResponse.MembersToRemoveCount,
                };
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.ThresholdExceeded)
            {
                aggregatedMembershipResponse = await context.CallActivityAsync<AggregatedMembershipUploadResponse>(nameof(AggregatedMembershipUploaderFunction),
                    new AggregatedMembershipUploadRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = currentPart,
                        TotalParts = totalParts,
                        GroupId = request.GroupId,
                        SourceMembershipFilePath = sourceMembershipFilePath,
                        CompressedMembersToAddJson = deltaResponse.CompressedMembersToAddJSON,
                        CompressedMembersToRemoveJson = deltaResponse.CompressedMembersToRemoveJSON,
                        CurrentUtcDateTime = currentUtcDateTime
                    });

                if (!aggregatedMembershipResponse.IsSuccessful)
                {
                    var errorMessage = string.IsNullOrWhiteSpace(aggregatedMembershipResponse.ErrorMessage)
                        ? "Aggregated membership upload failed without an error message."
                        : aggregatedMembershipResponse.ErrorMessage;

                    logger.AggregatedUploadError(errorMessage);

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    new JobStatusUpdaterRequest
                                                    {
                                                        SyncJob = request.SyncJob,
                                                        CurrentPart = currentPart,
                                                        TotalParts = totalParts,
                                                        Status = SyncStatus.Error,
                                                        IsDryRun = false,
                                                        IsNoOpSync = false
                                                    });

                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = currentPart,
                        TotalParts = totalParts,
                        JobStatus = SyncStatus.Error,
                        ResultStatus = ResultStatus.Failure
                    });

                    await DeleteMembershipFilesAsync(context, deltaCalculatorRequest);

                    return new MembershipSubOrchestratorResponse
                    {
                        MembershipDeltaStatus = MembershipDeltaStatus.Error
                    };
                }

                logger.UploadedMembershipFile(aggregatedMembershipResponse.FilePath, aggregatedMembershipResponse.MemberCount);

                SyncStatus status = SyncStatus.ThresholdExceeded;

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    Status = status,
                                                    IsDryRun = false,
                                                    IsNoOpSync = false,
                                                    ProposedUsersAdded = status == SyncStatus.ThresholdExceeded ? deltaResponse.MembersToAddCount : null,
                                                    ProposedUsersRemoved = status == SyncStatus.ThresholdExceeded ? deltaResponse.MembersToRemoveCount : null
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = status,
                    ResultStatus = ResultStatus.Success
                });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.DryRun)
            {
                logger.DryRunSyncComplete(request.GroupId, deltaResponse.MembersToAddCount, deltaResponse.MembersToRemoveCount);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    Status = SyncStatus.Idle,
                                                    IsDryRun = true,
                                                    IsNoOpSync = false
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = SyncStatus.Idle,
                    ResultStatus = ResultStatus.Success
                });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Error)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    Status = SyncStatus.Error,
                                                    IsDryRun = false,
                                                    IsNoOpSync = false
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = SyncStatus.Error,
                    ResultStatus = ResultStatus.Failure
                });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.NoChanges)
            {
                logger.NoMembershipChanges(request.GroupId);

                var sourceTypeCounts = JsonParser.GetQueryTypes(request.SyncJob.Query);
                var channelId = await context.CallActivityAsync<string>(nameof(GetChannelFunction), new GetChannelRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts
                });
                string identifier = null;
                if (_multilaneConfig.IsEnabled && request.SyncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
                {
                    var membersToBeUpdated = deltaResponse.MembersToAddCount + deltaResponse.MembersToRemoveCount;
                    identifier = membersToBeUpdated <= _multilaneConfig.Small ? "small" : "large";
                }

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
                    IsInitialSync = $"{request.SyncJob.LastRunTime == SqlDateTime.MinValue.Value}",
                    Identifier = identifier
                };

                var dbSyncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                       new JobReaderRequest
                                       {
                                           SyncJob = request.SyncJob,
                                           CurrentPart = currentPart,
                                           TotalParts = totalParts,
                                           JobId = request.SyncJob.Id
                                       });

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = SyncStatus.Idle,
                    ResultStatus = ResultStatus.Success
                });

                if (!context.IsReplaying)
                    SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(_telemetryClient, syncCompleteEvent, context.CurrentUtcDateTime, dbSyncJob.LastSuccessfulStartTime, "Success");

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = request.SyncJob,
                                    CurrentPart = currentPart,
                                    TotalParts = totalParts,
                                    Status = SyncStatus.Idle,
                                    IsDryRun = false,
                                    IsNoOpSync = true
                                });
            }

                await DeleteMembershipFilesAsync(context, deltaCalculatorRequest);

            return new MembershipSubOrchestratorResponse
            {
                MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus,
                MembersToBeAdded = deltaResponse.MembersToAddCount,
                MembersToBeRemoved = deltaResponse.MembersToRemoveCount
            };
        }

        private async Task DeleteMembershipFilesAsync(TaskOrchestrationContext context, DeltaCalculatorRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(request.SourceMembershipFilePath))
            {
                await context.CallActivityAsync(nameof(FileDeleterFunction), new FileDeleterRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    FilePath = request.SourceMembershipFilePath
                });
            }

            if (!string.IsNullOrWhiteSpace(request.DestinationMembershipFilePath))
            {
                await context.CallActivityAsync(nameof(FileDeleterFunction), new FileDeleterRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    FilePath = request.DestinationMembershipFilePath
                });
            }
        }

        private static bool HasCompleteMembershipSnapshot(
            IReadOnlyDictionary<int, string> completedParts,
            string destinationPart,
            int expectedPartCount)
        {
            if (completedParts == null ||
                completedParts.Count != expectedPartCount ||
                expectedPartCount <= 0 ||
                string.IsNullOrWhiteSpace(destinationPart))
            {
                return false;
            }

            var destinationFound = false;
            foreach (var part in completedParts)
            {
                if (part.Key <= 0 ||
                    part.Key > expectedPartCount ||
                    string.IsNullOrWhiteSpace(part.Value))
                {
                    return false;
                }

                if (string.Equals(part.Value, destinationPart, StringComparison.Ordinal))
                {
                    destinationFound = true;
                }
            }

            return destinationFound;
        }
    }
}
