// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class CacheUserUpdaterSubOrchestratorFunction
    {
        [Function(nameof(CacheUserUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("Hosts.GraphUpdater.CacheUserUpdaterSubOrchestratorFunction");
            var request = context.GetInput<CacheUserUpdaterRequest>();
            using var scope = logger.BeginGraphUpdaterScope(request);

            logger.FunctionStarted(nameof(CacheUserUpdaterSubOrchestratorFunction));
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
                    SyncJob = request.SyncJob
                });

                if (cacheChecker.BlobStatus == BlobStatus.Found)
                {
                    await context.CallActivityAsync(nameof(CacheUpdaterFunction), new CacheUpdaterRequest
                    {
                        CacheFilePath = cacheChecker.Path,
                        SyncJob = request.SyncJob,
                        UserIds = request.UserIds,
                        GroupId = request.GroupId,
                        Timestamp = request.SyncJob.LastSuccessfulStartTime
                    });
                }

                logger.FunctionCompleted(nameof(CacheUserUpdaterSubOrchestratorFunction));
            }
            catch (FileNotFoundException fe)
            {
                logger.CacheUpdaterFileNotFound(fe.Message);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.FileNotFound,
                                                    SyncJob = request.SyncJob
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.FileNotFound, ResultStatus = ResultStatus.Failure, SyncJob = request.SyncJob });

                throw;
            }
            catch (Exception ex)
            {
                logger.CacheUpdaterUnexpectedException(ex, ex.Message);

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.Error,
                                                    SyncJob = request.SyncJob
                                                });

                throw;
            }
        }
    }
}
