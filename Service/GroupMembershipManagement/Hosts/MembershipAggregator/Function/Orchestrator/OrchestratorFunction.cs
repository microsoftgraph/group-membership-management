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
                    IncrementThresholdViolations = false,
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
            var hasSourceCompleted = false;

            try
            {
                logger.GroupIdRetrieved(request.SyncJob.Id, groupId);

                await using (await context.Entities.LockEntitiesAsync(new List<EntityInstanceId> { entityInstanceId }))
                {
                    await context.Entities.CallEntityAsync(entityInstanceId, nameof(JobTrackerEntity.SetTotalParts), input: request.PartsCount);
                    await context.Entities.CallEntityAsync(entityInstanceId, nameof(JobTrackerEntity.AddCompletedPart), input: request.FilePath);

                    if (request.IsDestinationPart)
                        await context.Entities.CallEntityAsync(entityInstanceId, nameof(JobTrackerEntity.SetDestinationPart), input: request.FilePath);

                    hasSourceCompleted = await context.Entities.CallEntityAsync<bool>(entityInstanceId, nameof(JobTrackerEntity.IsComplete));
                }

                if (hasSourceCompleted)
                {
                    logger.FunctionStarted(nameof(OrchestratorFunction));

                    var membershipResponse = await context.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                                                            (
                                                                                nameof(MembershipSubOrchestratorFunction),
                                                                                new MembershipSubOrchestratorRequest
                                                                                {
                                                                                    EntityId = entityInstanceId,
                                                                                    SyncJob = request.SyncJob,
                                                                                    GroupId = groupId,
                                                                                    CurrentPart = currentPart,
                                                                                    TotalParts = totalParts
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
                                                    IncrementThresholdViolations = false,
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
                                                    IncrementThresholdViolations = false,
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