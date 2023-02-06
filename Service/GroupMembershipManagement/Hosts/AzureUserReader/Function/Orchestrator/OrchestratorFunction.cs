// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models.Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public OrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started" }, VerbosityLevel.DEBUG);

            try
            {
                var request = context.GetInput<AzureUserReaderRequest>();
                var personnelNumbers = await context.CallActivityAsync<IList<string>>(nameof(PersonnelNumberReaderFunction), request);
                var users = await context.CallSubOrchestratorAsync<List<GraphProfileInformation>>(nameof(UserReaderSubOrchestratorFunction), personnelNumbers);
                var missingUsers = new HashSet<string>(personnelNumbers).Except(users.Select(x => x.PersonnelNumber)).ToList();

                if (request.ShouldCreateNewUsers && missingUsers.Count > 0)
                {
                    var userCreatorRequest = new AzureUserCreatorRequest
                    {
                        PersonnelNumbers = missingUsers,
                        TenantInformation = request.TenantInformation
                    };

                    var newUsers = await context.CallSubOrchestratorAsync<List<GraphProfileInformation>>(nameof(UserCreatorSubOrchestratorFunction), userCreatorRequest);
                    users.AddRange(newUsers);
                }

                if (users.Count > 0)
                    await context.CallActivityAsync(nameof(UploadUsersFunction), new UploadUsersRequest { AzureUserReaderRequest = request, Users = users });
            }
            catch (Exception ex)
            {
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} failed with exception:\n{ex}" });
                throw;
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}