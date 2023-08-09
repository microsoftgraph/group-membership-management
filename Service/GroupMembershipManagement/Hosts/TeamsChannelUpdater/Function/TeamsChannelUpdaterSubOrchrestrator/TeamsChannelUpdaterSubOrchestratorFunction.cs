// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.ApplicationInsights;
using Repositories.Contracts;
using Models.Entities;

namespace Hosts.TeamsChannelUpdater
{
    public class TeamsChannelUpdaterSubOrchestratorFunction
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly int _batchSize = 100;

        public TeamsChannelUpdaterSubOrchestratorFunction(TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(TeamsChannelUpdaterSubOrchestratorFunction))]
        public async Task<TeamsChannelUpdaterSubOrchestratorResponse> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var skip = 0;
            var request = context.GetInput<TeamsChannelUpdaterSubOrchestratorRequest>();
            var totalSuccessCount = 0;
            var allUsersNotFound = new List<AzureADTeamsUser>();

            if (request == null)
            {
                return new TeamsChannelUpdaterSubOrchestratorResponse();
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest { Message = $"{nameof(TeamsChannelUpdaterSubOrchestratorFunction)} function started with batch size {_batchSize}", RunId = request.RunId, Verbosity = VerbosityLevel.INFO });

            var batch = request.Members?.Skip(skip).Take(_batchSize).ToList() ?? new List<AzureADTeamsUser>();

            var retryMembers = new List<AzureADTeamsUser>();

            // The orchestrator will stop trying to retry user operations once the retry count exceeds the original member count
            while (batch.Count > 0)
            {
                var response = await context.CallActivityAsync<TeamsUpdaterResponse>(nameof(TeamsUpdaterFunction),
                                           new TeamsUpdaterRequest
                                           {
                                               Type = request.Type,
                                               Members = batch,
                                               TeamsChannelInfo = request.TeamsChannelInfo,
                                               RunId = request.RunId
                                           });
                totalSuccessCount += response.SuccessCount;

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount}/{request.Members.Count} users so far.",
                                                    RunId = request.RunId
                                                });


                skip += batch.Count;

                batch = request.Members.Skip(skip).Take(_batchSize).ToList();
                retryMembers.AddRange(response.UsersToRetry);
                allUsersNotFound.AddRange(response.UsersNotFound);
            }

            skip = 0;
            var retryBatch = retryMembers?.Skip(skip).Take(_batchSize).ToList() ?? new List<AzureADTeamsUser>();
            var userFailures = new List<AzureADTeamsUser>();

            if (retryBatch.Count > 0)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = $"Retrying {retryBatch.Count} users",
                        RunId = request.RunId
                    });

                while (retryBatch.Count > 0)
                {
                    var response = await context.CallActivityAsync<TeamsUpdaterResponse>(nameof(TeamsUpdaterFunction),
                                               new TeamsUpdaterRequest
                                               {
                                                   Type = request.Type,
                                                   Members = batch,
                                                   TeamsChannelInfo = request.TeamsChannelInfo,
                                                   RunId = request.RunId
                                               });
                    totalSuccessCount += response.SuccessCount;

                    await context.CallActivityAsync(nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount}/{request.Members.Count} users so far.",
                            RunId = request.RunId
                        });


                    skip += retryBatch.Count;

                    retryBatch = retryMembers.Skip(skip).Take(_batchSize).ToList();
                    userFailures.AddRange(response.UsersToRetry);
                    allUsersNotFound.AddRange(response.UsersNotFound);
                }
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount} users in total, " +
                    $"{allUsersNotFound.Count} users not found, {userFailures.Count} users failed.",
                    RunId = request.RunId
                });

            if (!context.IsReplaying)
            {
                _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersNotFound), request.Members.Count - totalSuccessCount);

                if (request.IsInitialSync)
                {
                    if (request.Type == RequestType.Add)
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersAddedFromOnboarding), totalSuccessCount);
                    else
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersRemovedFromOnboarding), totalSuccessCount);
                }
                else
                {
                    if (request.Type == RequestType.Add)
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersAdded), totalSuccessCount);
                    else
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersRemoved), totalSuccessCount);
                }
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                      new LoggerRequest
                                                      {
                                                          Message = $"{nameof(TeamsChannelUpdaterSubOrchestratorFunction)} function completed",
                                                          RunId = request.RunId,
                                                          Verbosity = VerbosityLevel.DEBUG
                                                      });

            return new TeamsChannelUpdaterSubOrchestratorResponse()
            {
                Type = request.Type,
                SuccessCount = totalSuccessCount,
                UsersNotFound = allUsersNotFound,
                UsersFailed = userFailures
            };
        }
    }
}