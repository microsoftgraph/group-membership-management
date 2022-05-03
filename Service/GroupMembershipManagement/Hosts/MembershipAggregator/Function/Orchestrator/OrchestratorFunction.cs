// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class OrchestratorFunction
    {
        private readonly IConfiguration _configuration;

        public OrchestratorFunction(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<MembershipAggregatorHttpRequest>();
            var syncJobProperties = request.SyncJob.ToDictionary();

            try
            {
                var runId = request.SyncJob.RunId;
                var entityId = new EntityId(nameof(JobTrackerEntity), $"{request.SyncJob.TargetOfficeGroupId}_{runId}");
                var proxy = context.CreateEntityProxy<IJobTracker>(entityId);
                var hasSourceCompleted = false;

                using (await context.LockAsync(entityId))
                {
                    await proxy.SetTotalParts(request.PartsCount);
                    await proxy.AddCompletedPart(request.FilePath);
                    hasSourceCompleted = await proxy.IsComplete();
                }

                if (hasSourceCompleted)
                {
                    var state = await proxy.GetState();
                    var downloadFileTasks = new List<Task<string>>();
                    foreach (var part in state.CompletedParts)
                    {
                        var downloadRequest = new FileDownloaderRequest { FilePath = part, SyncJob = request.SyncJob };
                        downloadFileTasks.Add(context.CallActivityAsync<string>(nameof(FileDownloaderFunction), downloadRequest));
                    }

                    var groupMemberships = (await Task.WhenAll(downloadFileTasks))
                                            .Select(x => JsonConvert.DeserializeObject<GroupMembership>(x))
                                            .ToList();

                    var allMembers = groupMemberships.SelectMany(x => x.SourceMembers).Distinct().ToList();
                    groupMemberships[0].SourceMembers = allMembers;

                    var timeStamp = request.SyncJob.Timestamp.ToString("MMddyyyy-HHmmss");
                    var filePath = $"/{groupMemberships[0].Destination.ObjectId}/{timeStamp}_{runId}_Aggregated.json";
                    var content = JsonConvert.SerializeObject(groupMemberships[0]);
                    var uploadRequest = new FileUploaderRequest { FilePath = filePath, Content = content, SyncJob = request.SyncJob };

                    await context.CallActivityAsync(nameof(FileUploaderFunction), uploadRequest);
                    await proxy.Delete();

                    var updateRequestContent = new MembershipHttpRequest { FilePath = filePath, SyncJob = request.SyncJob };
                    var updateRequest = new DurableHttpRequest(HttpMethod.Post,
                                                                new Uri(_configuration["graphUpdaterUrl"]),
                                                                content: JsonConvert.SerializeObject(updateRequestContent),
                                                                headers: new Dictionary<string, StringValues> { { "x-functions-key", _configuration["graphUpdaterFunctionKey"] } },
                                                                httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(30), 3));

                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                    new LogMessage
                                                    {
                                                        Message = "Calling GraphUpdater",
                                                        DynamicProperties = syncJobProperties
                                                    });

                    var response = await context.CallHttpAsync(updateRequest);

                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                    new LogMessage
                                                    {
                                                        Message = $"GraphUpdater response Code: {response.StatusCode}, Content: {response.Content}",
                                                        DynamicProperties = syncJobProperties
                                                    });

                    if (response.StatusCode != HttpStatusCode.NoContent)
                    {
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                        new JobStatusUpdaterRequest { SyncJob = request.SyncJob, Status = SyncStatus.Error });
                    }
                }
            }
            catch (FileNotFoundException fe)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LogMessage { Message = fe.Message, DynamicProperties = syncJobProperties });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    Status = SyncStatus.FileNotFound,
                                                    SyncJob = request.SyncJob
                                                });

                throw;
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LogMessage { Message = $"Unexpected exception. {ex}", DynamicProperties = syncJobProperties });

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