// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights;

namespace Hosts.AzureMembershipProvider
{
    public class SubOrchestratorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly TelemetryClient _telemetryClient;

        public SubOrchestratorFunction(ILoggingRepository loggingRepository, TelemetryClient telemetryClient)
        {
            _log = loggingRepository;
            _telemetryClient = telemetryClient;
        }

        [FunctionName(nameof(SubOrchestratorFunction))]
        public async Task<(List<AzureADUser> Users, SyncStatus Status)> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<SubOrchestratorRequest>();
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            if (request != null)
            {
                _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

                if (request.Url.Contains("places") && request.Url.Contains("room"))
                {
                    var response = await context.CallActivityAsync<PlaceInformation>(nameof(RoomsReaderFunction), new RoomsReaderRequest { Url = request.Url, Top = 100, Skip = 0, RunId = request.RunId });
                    allUsers.AddRange(response.Users);
                }
                else if (request.Url.Contains("places") && request.Url.Contains("workspace"))
                {
                    var response = await context.CallActivityAsync<PlaceInformation>(nameof(WorkSpacesReaderFunction), new WorkSpacesReaderRequest { Url = request.Url, Top = 100, Skip = 0, RunId = request.RunId });
                    allUsers.AddRange(response.Users);
                }
                else if (request.Url.Contains("users"))
                {
                    var userResponse = await context.CallActivityAsync<UserInformation>(nameof(UsersReaderFunction), new UsersReaderRequest { Url = request.Url, RunId = request.RunId });
                    allUsers.AddRange(userResponse.Users);
                    userResponse.NonUserGraphObjects.ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));
                    while (!string.IsNullOrEmpty(userResponse.NextPageUrl))
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page for url: {request.Url}" });
                        userResponse = await context.CallActivityAsync<UserInformation>(nameof(SubsequentUsersReaderFunction), new SubsequentUsersReaderRequest { RunId = request.RunId, NextPageUrl = userResponse.NextPageUrl, UsersFromPage = userResponse.UsersFromPage });
                        allUsers.AddRange(userResponse.Users);
                        userResponse.NonUserGraphObjects.ToList().ForEach(x =>
                        {
                            if (allNonUserGraphObjects.ContainsKey(x.Key))
                                allNonUserGraphObjects[x.Key] += x.Value;
                            else
                                allNonUserGraphObjects[x.Key] = x.Value;
                        });
                    }
                }
                else
                {
                    _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Url {request.Url} not supported" });
                    return (allUsers, SyncStatus.Error);
                }
                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Read {allUsers.Count} users" });
            }
            _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return (allUsers, SyncStatus.InProgress);
        }
    }
}