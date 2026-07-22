// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Services.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class OrchestratorFunction
    {
        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<MembershipAggregatorHttpRequest>();
            var runId = request.SyncJob.RunId ?? Guid.Empty;
            var currentPart = request.PartNumber;
            var totalParts = request.PartsCount;

            var logger = context.CreateReplaySafeLogger("MembershipAggregator.OrchestratorFunction");
            using var scope = logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = currentPart,
                ["TotalParts"] = totalParts
            });

            var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), new GetGroupRequest
            {
                SyncJob = request.SyncJob,
                CurrentPart = currentPart,
                TotalParts = totalParts
            });

            if (groupId.Equals(Guid.Empty))
            {
                logger.UnableToGetGroupId(request.SyncJob.Id);
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest
                {
                    Status = SyncStatus.Error,
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
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
                return;
            }

            var entityInstanceId = new EntityInstanceId(nameof(JobTrackerEntity), $"{request.SyncJob.Id}_{runId}");
            var allPartsCompleted = false;

            try
            {
                if (currentPart <= 0 ||
                    totalParts <= 0 ||
                    currentPart > totalParts ||
                    string.IsNullOrEmpty(request.FilePath))
                {
                    logger.InvalidPartRegistration(request.SyncJob.Id, currentPart, totalParts, request.FilePath);
                    throw new ArgumentException(
                        $"Invalid part registration: PartNumber={currentPart}, " +
                        $"TotalParts={totalParts}, FilePath='{request.FilePath}'.");
                }

                logger.GroupIdRetrieved(request.SyncJob.Id, groupId);

                // Single atomic entity op.
                var registration = new JobTrackerRegistration
                {
                    PartNumber        = request.PartNumber,
                    TotalParts        = request.PartsCount,
                    FilePath          = request.FilePath,
                    IsDestinationPart = request.IsDestinationPart
                };

                var completion = await context.Entities.CallEntityAsync<JobTrackerCompletionResult>(
                    entityInstanceId,
                    nameof(JobTrackerEntity.RegisterPartAndCheckComplete),
                    input: registration);

                allPartsCompleted = completion.IsComplete;

                logger.PartRegistered(
                    request.SyncJob.Id,
                    request.PartNumber,
                    completion.CompletedCount,
                    completion.TotalParts,
                    completion.IsComplete);

                if (allPartsCompleted)
                {
                    logger.FunctionStarted(nameof(OrchestratorFunction));

                    var membershipResponse = await context.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                                                            (
                                                                                nameof(MembershipSubOrchestratorFunction),
                                                                                new MembershipSubOrchestratorRequest
                                                                                {
                                                                                    SyncJob = request.SyncJob,
                                                                                    GroupId = groupId,
                                                                                    CurrentPart = currentPart,
                                                                                    TotalParts = completion.TotalParts,
                                                                                    CompletedParts = completion.CompletedParts == null
                                                                                        ? new Dictionary<int, string>()
                                                                                        : new Dictionary<int, string>(completion.CompletedParts),
                                                                                    DestinationPart = completion.DestinationPart
                                                                                }
                                                                            );

                    if (membershipResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
                    {
                        var membershipHttpRequest = new MembershipHttpRequest
                        {
                            FilePath = membershipResponse.FilePath,
                            SyncJob = request.SyncJob,
                            GroupId = groupId,
                            ProjectedMemberCount = membershipResponse.ProjectedMemberCount,
                            MembersToBeAdded = membershipResponse.MembersToBeAdded,
                            MembersToBeRemoved = membershipResponse.MembersToBeRemoved
                        };

                        await context.CallActivityAsync(nameof(TopicMessageSenderFunction), new TopicMessageSenderRequest
                        {
                            MembershipHttpRequest = membershipHttpRequest,
                            SyncJob = request.SyncJob,
                            CurrentPart = currentPart,
                            TotalParts = totalParts
                        });
                    }

                    logger.FunctionCompleted(nameof(OrchestratorFunction));
                }
            }
            catch (FileNotFoundException fe)
            {
                logger.OrchestratorFileNotFound(fe);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.FileNotFound,
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
                                                    IsDryRun = false,
                                                    IsNoOpSync = false
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                    JobStatus = SyncStatus.FileNotFound,
                    ResultStatus = ResultStatus.Failure
                });

                throw;
            }
            catch (Exception ex)
            {
                logger.OrchestratorUnexpectedException(ex);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.Error,
                                                    SyncJob = request.SyncJob,
                                                    CurrentPart = currentPart,
                                                    TotalParts = totalParts,
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

                throw;
            }
        }
    }
}