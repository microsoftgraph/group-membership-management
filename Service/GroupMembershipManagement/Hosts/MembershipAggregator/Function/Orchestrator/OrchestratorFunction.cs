// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
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

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<MembershipAggregatorHttpRequest>();
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            var entityId = new EntityId(nameof(JobTrackerEntity), $"{request.SyncJob.TargetOfficeGroupId}_{runId}");
            var proxy = context.CreateEntityProxy<IJobTracker>(entityId);
            var hasSourceCompleted = false;
            var errorOccurred = false;

            try
            {
                using (await context.LockAsync(entityId))
                {
                    await proxy.SetTotalParts(request.PartsCount);
                    await proxy.AddCompletedPart(request.FilePath);
                    hasSourceCompleted = await proxy.IsComplete();

                    if (request.IsDestinationPart)
                        await proxy.SetDestinationPart(request.FilePath);
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

                        var updateRequest = new DurableHttpRequest(HttpMethod.Post,
                                                                    new Uri(_configuration["graphUpdaterUrl"]),
                                                                    content: JsonConvert.SerializeObject(updateRequestContent),
                                                                    headers: new Dictionary<string, StringValues>
                                                                                { { "x-functions-key", _configuration["graphUpdaterFunctionKey"] } },
                                                                    httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(30), 3));

                        await context.CallActivityAsync(nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                Message = new LogMessage
                                {
                                    Message = "Calling GraphUpdater",
                                    RunId = runId
                                }
                            });

                        var response = await context.CallHttpAsync(updateRequest);

                        await context.CallActivityAsync(nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                Message = new LogMessage
                                {
                                    Message = $"GraphUpdater response Code: {response.StatusCode}, Content: {response.Content}",
                                    RunId = runId
                                }
                            });

                        if (response.StatusCode != HttpStatusCode.NoContent)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                            new JobStatusUpdaterRequest { SyncJob = request.SyncJob, Status = SyncStatus.Error });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                        }
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
                    await proxy.Delete();
                }

                _loggingRepository.RemoveSyncJobProperties(runId);
            }
        }
    }
}