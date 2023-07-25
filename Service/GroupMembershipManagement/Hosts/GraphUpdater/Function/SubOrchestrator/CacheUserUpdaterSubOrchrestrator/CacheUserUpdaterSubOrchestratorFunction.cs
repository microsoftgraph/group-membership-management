// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ServiceBus;
using GraphUpdater.Entities;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Models.Helpers;

namespace Hosts.GraphUpdater
{
    public class CacheUserUpdaterSubOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient = null;

        public CacheUserUpdaterSubOrchestratorFunction(ILoggingRepository loggingRepository, TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(CacheUserUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {

            var request = context.GetInput<CacheUserUpdaterRequest>();

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                     new LoggerRequest
                                                     {
                                                         Message = $"{nameof(CacheUserUpdaterSubOrchestratorFunction)} function started",
                                                         SyncJob = request.SyncJob
                                                     });
            try
            {
                if (request == null || request.GroupId.ToString() == null)
                {
                    return;
                }

                var filePath = $"cache/{request.GroupId}";
                var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction), new FileDownloaderRequest
                {
                    FilePath = filePath,
                    SyncJob = request.SyncJob
                });

                if (!string.IsNullOrEmpty(fileContent))
                {
                    await context.CallActivityAsync(nameof(CacheUpdaterFunction), new CacheUpdaterRequest
                    {
                        FileContent = fileContent,
                        RunId = request.SyncJob.RunId,
                        UserIds = request.UserIds,
                        GroupId = request.GroupId,
                        Timestamp = request.SyncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss")
                    });
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                     new LoggerRequest
                                                     {
                                                         Message = $"{nameof(CacheUserUpdaterSubOrchestratorFunction)} function completed",
                                                         SyncJob = request.SyncJob
                                                     });
            }

            catch (FileNotFoundException fe)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                     new LoggerRequest
                                                     {
                                                         Message = fe.Message,
                                                         SyncJob = request.SyncJob
                                                     });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.FileNotFound
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.FileNotFound, ResultStatus = ResultStatus.Failure, RunId = request.RunId });

                throw;
            }


            catch (Exception ex)
            {

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = $"Unexpected exception. {ex}",
                        SyncJob = request.SyncJob
                    });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.Error,
                                                    RunId = (Guid)request.SyncJob.RunId
                                                });

                throw;
            }
        }
    }
}