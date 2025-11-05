// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Models;
using Repositories.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class OrchestratorFunction
    {
        private readonly IConfiguration _configuration;
        private readonly ILoggingRepository _loggingRepository;

        public OrchestratorFunction(IConfiguration configuration, ILoggingRepository loggingRepository)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<MembershipAggregatorHttpRequest>();
            var runId = request.SyncJob.RunId ?? Guid.Empty;
            var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), request.SyncJob);
            if (groupId.Equals(Guid.Empty))
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = new LogMessage { Message = $"Unable to get group id for job:{request.SyncJob.Id}", RunId = runId}, Verbosity = VerbosityLevel.DEBUG });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { 
                    Status = SyncStatus.Error, 
                    SyncJob = request.SyncJob,
                    IsDryRun = false,
                    IncrementThresholdViolations = false,
                    IsNoOpSync = false
                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                return;
            }
            var entityInstanceId = new EntityInstanceId(nameof(JobTrackerEntity), $"{request.SyncJob.Id}_{runId}");
            var hasSourceCompleted = false;

            try
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"Group Id for job:{request.SyncJob.Id} is {groupId}",
                            RunId = runId
                        },
                        Verbosity = VerbosityLevel.DEBUG
                    });

                await using (await context.Entities.LockEntitiesAsync(new List<EntityInstanceId> { entityInstanceId }))
                {
                    await context.Entities.CallEntityAsync(entityInstanceId, nameof(JobTrackerEntity.SetTotalParts), input: request.PartsCount);
                    await context.Entities.CallEntityAsync(entityInstanceId, nameof(JobTrackerEntity.AddCompletedPart), input: request.FilePath);
                    hasSourceCompleted = await context.Entities.CallEntityAsync<bool>(entityInstanceId, nameof(JobTrackerEntity.IsComplete));

                    if (request.IsDestinationPart)
                        await context.Entities.CallEntityAsync(entityInstanceId, nameof(JobTrackerEntity.SetDestinationPart), input: request.FilePath);

                }

                if (hasSourceCompleted)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            Message = new LogMessage
                            {
                                Message = $"{nameof(OrchestratorFunction)} function started",
                                RunId = runId
                            },
                            Verbosity = VerbosityLevel.DEBUG
                        });

                    var membershipResponse = await context.CallSubOrchestratorAsync<MembershipSubOrchestratorResponse>
                                                                            (
                                                                                nameof(MembershipSubOrchestratorFunction),
                                                                                new MembershipSubOrchestratorRequest
                                                                                {
                                                                                    EntityId = entityInstanceId,
                                                                                    SyncJob = request.SyncJob,
                                                                                    GroupId = groupId
                                                                                }
                                                                            );

                    if (membershipResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
                    {
                        var updateRequestContent = new MembershipHttpRequest
                        {
                            FilePath = membershipResponse.FilePath,
                            SyncJob = request.SyncJob,
                            GroupId = groupId,
                            ProjectedMemberCount = membershipResponse.ProjectedMemberCount,
                            MembersToBeAdded = membershipResponse.MembersToBeAdded,
                            MembersToBeRemoved = membershipResponse.MembersToBeRemoved
                        };

                        await context.CallActivityAsync(nameof(TopicMessageSenderFunction), updateRequestContent);
                    }

                    await context.CallActivityAsync(nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            Message = new LogMessage
                            {
                                Message = $"{nameof(OrchestratorFunction)} function completed",
                                RunId = runId
                            },
                            Verbosity = VerbosityLevel.DEBUG
                        });
                }
            }
            catch (FileNotFoundException fe)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage { Message = fe.Message, RunId = runId },
                        Verbosity = VerbosityLevel.INFO
                    });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.FileNotFound,
                                                    SyncJob = request.SyncJob,
                                                    IsDryRun = false,
                                                    IncrementThresholdViolations = false,
                                                    IsNoOpSync = false
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.FileNotFound, ResultStatus = ResultStatus.Failure, RunId = runId });

                throw;
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage { Message = $"Unexpected exception. {ex}", RunId = runId },
                        Verbosity = VerbosityLevel.INFO
                    });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.Error,
                                                    SyncJob = request.SyncJob,
                                                    IsDryRun = false,
                                                    IncrementThresholdViolations = false,
                                                    IsNoOpSync = false
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });

                throw;
            }
            finally
            {               
                _loggingRepository.RemoveSyncJobProperties(runId);
            }
        }
    }
}