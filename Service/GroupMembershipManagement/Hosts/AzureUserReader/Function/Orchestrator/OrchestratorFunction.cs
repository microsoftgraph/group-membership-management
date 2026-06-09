// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class OrchestratorFunction
    {
        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("AzureUserReader.OrchestratorFunction");
            logger.FunctionStarted(nameof(OrchestratorFunction));

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
                logger.FunctionFailed(nameof(OrchestratorFunction), ex);
                throw;
            }

            logger.FunctionCompleted(nameof(OrchestratorFunction));
        }
    }
}