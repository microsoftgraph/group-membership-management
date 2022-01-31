// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;

namespace Hosts.AzureUserReader
{
    public class UserCreatorSubOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public UserCreatorSubOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(UserCreatorSubOrchestratorFunction))]
        public async Task<List<GraphProfileInformation>> CreateUsersAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<AzureUserCreatorRequest>();
            var profiles = new List<GraphProfileInformation>();

            var skip = 0;
            var take = 500;
            var createrTasks = new List<Task<List<GraphProfileInformation>>>();
            var usersCreated = 0;
            List<string> batch;

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Creating {request.PersonnelNumbers.Count} new users.",
                    RunId = null
                });

            while ((batch = request.PersonnelNumbers.Skip(skip).Take(take).ToList()).Count > 0)
            {
                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Processing {skip + take} out of {request.PersonnelNumbers.Count} users.",
                        RunId = null
                    });

                var userCreatorRequest = new AzureUserCreatorRequest
                {
                    PersonnelNumbers = batch,
                    TenantInformation = request.TenantInformation
                };

                var newProfiles = await context.CallActivityAsync<List<GraphProfileInformation>>(nameof(AzureUserCreatorFunction), userCreatorRequest);
                profiles.AddRange(newProfiles);

                skip += take;
                usersCreated += newProfiles.Count;
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Created {usersCreated} new users.",
                    RunId = null
                });

            return profiles;
        }
    }
}