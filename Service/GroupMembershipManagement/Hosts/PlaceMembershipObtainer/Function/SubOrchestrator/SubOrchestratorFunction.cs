// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights;
using Models;
using System;

namespace Hosts.PlaceMembershipObtainer
{
    public class SubOrchestratorFunction
    {
        private readonly TelemetryClient _telemetryClient;

        public SubOrchestratorFunction(TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [Function(nameof(SubOrchestratorFunction))]
        public async Task<SubOrchestratorResponse> RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger($"PlaceMembershipObtainer.{nameof(SubOrchestratorFunction)}");
            var request = context.GetInput<SubOrchestratorRequest>();
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            if (request != null)
            {
                using (logger.BeginRunIdScope(request.RunId))
                {
                    logger.FunctionStarted(nameof(SubOrchestratorFunction));

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
                            logger.GettingResultsFromNextPage(userResponse.NextPageUrl);
                            userResponse = await context.CallActivityAsync<UserInformation>(nameof(SubsequentUsersReaderFunction), new SubsequentUsersReaderRequest { RunId = request.RunId, NextPageUrl = userResponse.NextPageUrl });
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
                        logger.UrlNotSupported(request.Url);
                        return (new SubOrchestratorResponse { Users = allUsers, Status = SyncStatus.Error });
                    }
                    logger.ReadUsersCount(allUsers.Count);

                    logger.FunctionCompleted(nameof(SubOrchestratorFunction));
                }
            }
            return (new SubOrchestratorResponse { Users = allUsers, Status = SyncStatus.InProgress });
        }
    }
}