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
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            var entityId = new EntityInstanceId(nameof(JobTrackerEntity), $"{request.SyncJob.TargetOfficeGroupId}_{runId}");
            var hasSourceCompleted = false;
            var errorOccurred = false;

            try
            {
                await using (await context.Entities.LockEntitiesAsync(entityId))
                {
                    await context.Entities.CallEntityAsync(entityId, "SetTotalParts", request.PartsCount);
                    await context.Entities.CallEntityAsync(entityId, "AddCompletedPart", request.FilePath);
                    hasSourceCompleted = await context.Entities.CallEntityAsync<bool>(entityId, "IsComplete");

                    if (request.IsDestinationPart)
                        await context.Entities.CallEntityAsync(entityId, "SetDestinationPart", request.FilePath);
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
                                                                                    EntityId = entityId,
                                                                                    SyncJob = request.SyncJob
                                                                                }
                                                                            );

                    if (membershipResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
                    {
                        var updateRequestContent = new MembershipHttpRequest
                        {
                            FilePath = membershipResponse.FilePath,
                            SyncJob = request.SyncJob,
                            ProjectedMemberCount = membershipResponse.ProjectedMemberCount
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
                errorOccurred = true;

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage { Message = fe.Message, RunId = runId }
                    });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.FileNotFound,
                                                    SyncJob = request.SyncJob
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.FileNotFound, ResultStatus = ResultStatus.Failure, RunId = runId });

                throw;
            }
            catch (Exception ex)
            {
                errorOccurred = true;

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage { Message = $"Unexpected exception. {ex}", RunId = runId }
                    });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.Error,
                                                    SyncJob = request.SyncJob
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });

                throw;
            }
            finally
            {
                if (hasSourceCompleted || errorOccurred)
                {
                    await context.Entities.CallEntityAsync(entityId, "Delete");
                }

                _loggingRepository.RemoveSyncJobProperties(runId);
            }
        }
    }
}