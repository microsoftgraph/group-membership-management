// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using System;
using System.IO;
using System.Threading.Tasks;


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

                var filePrefixPath = CacheFileNaming.BuildCacheFileNamePrefix(request.GroupId);
                var cacheChecker = await context.CallActivityAsync<BlobResult>(nameof(BlobCheckerFunction), new BlobCheckerRequest
                {
                    Prefix = filePrefixPath,
                    RunId = request.SyncJob.RunId.Value
                });

                if (cacheChecker.BlobStatus == BlobStatus.Found)
                {
                    await context.CallActivityAsync(nameof(CacheUpdaterFunction), new CacheUpdaterRequest
                    {
                        CacheFilePath = cacheChecker.Path,
                        RunId = request.SyncJob.RunId,
                        UserIds = request.UserIds,
                        GroupId = request.GroupId,
                        Timestamp = request.SyncJob.LastSuccessfulStartTime
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