// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;

namespace Hosts.AzureUserReader
{
    public class UserCreatorSubOrchestratorFunction
    {
        [Function(nameof(UserCreatorSubOrchestratorFunction))]
        public async Task<List<GraphProfileInformation>> CreateUsersAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("AzureUserReader.UserCreatorSubOrchestratorFunction");
            var request = context.GetInput<AzureUserCreatorRequest>();
            var profiles = new List<GraphProfileInformation>();

            var skip = 0;
            var take = 500;
            var usersCreated = 0;
            List<string> batch;

            if (request.PersonnelNumbers == null || !request.PersonnelNumbers.Any())
            {
                logger.NoPersonnelNumbersProvided();
                return new List<GraphProfileInformation>();
            }

            logger.CreatingNewUsers(request.PersonnelNumbers.Count);

            while ((batch = request.PersonnelNumbers.Skip(skip).Take(take).ToList()).Count > 0)
            {
                logger.ProcessingUsers(Math.Min(skip + take, request.PersonnelNumbers.Count), request.PersonnelNumbers.Count);

                var userCreatorRequest = new AzureUserCreatorRequest
                {
                    PersonnelNumbers = batch,
                    TenantInformation = request.TenantInformation,
                    RequestId = context.InstanceId
                };

                var logDetails = new
                {
                    RequestId = userCreatorRequest.RequestId,
                    TenantDomain = userCreatorRequest.TenantInformation?.TenantDomain,
                    PersonnelCount = userCreatorRequest.PersonnelNumbers?.Count ?? 0
                };
                logger.UserCreatorRequestDetails(Newtonsoft.Json.JsonConvert.SerializeObject(logDetails));

                var newProfiles = await context.CallActivityAsync<List<GraphProfileInformation>>(nameof(AzureUserCreatorFunction), userCreatorRequest);
                profiles.AddRange(newProfiles);

                skip += take;
                usersCreated += newProfiles.Count;
            }

            logger.NewUsersCreated(usersCreated);

            return profiles;
        }
    }
}