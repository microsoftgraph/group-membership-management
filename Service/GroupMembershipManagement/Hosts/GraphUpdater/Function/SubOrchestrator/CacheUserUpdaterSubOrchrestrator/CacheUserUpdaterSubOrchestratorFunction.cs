// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using GraphUpdater.Entities;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUserUpdaterSubOrchestratorFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

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
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                       new LoggerRequest
                                                       {
                                                           Message = $"{request.UserIds.Count} users to remove from cache/{request.GroupId}",
                                                           SyncJob = request.SyncJob,
                                                           Verbosity = VerbosityLevel.DEBUG
                                                       });
                    var json = JsonConvert.DeserializeObject<GroupMembership>(fileContent);
                    var cacheMembers = json.SourceMembers.Distinct().ToList();
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                       new LoggerRequest
                                                       {
                                                           Message = $"Earlier count in cache/{request.GroupId}: {cacheMembers.Count}",
                                                           SyncJob = request.SyncJob,
                                                           Verbosity = VerbosityLevel.DEBUG
                                                       });
                    var newUsers = cacheMembers.Except(request.UserIds).ToList();
                    await context.CallActivityAsync(nameof(FileUploaderFunction),
                                                            new FileUploaderRequest
                                                            {
                                                                SyncJob = request.SyncJob,
                                                                ObjectId = request.GroupId,
                                                                Users = newUsers
                                                            });
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                       new LoggerRequest
                                                       {
                                                           Message = $"New count in cache/{request.GroupId}: {newUsers.Count}",
                                                           SyncJob = request.SyncJob,
                                                           Verbosity = VerbosityLevel.DEBUG
                                                       });
                }

                if (!context.IsReplaying) _ = _loggingRepository.LogMessageAsync(
                    new LogMessage
                    {
                        Message = $"{nameof(CacheUserUpdaterSubOrchestratorFunction)} function completed",
                        RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
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