// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class AzureUserCreatorFunction
    {
        private readonly IGraphUserRepository _graphUserRepository = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public AzureUserCreatorFunction(IGraphUserRepository graphUserRepository, ILoggingRepository loggingRepository)
        {
            _graphUserRepository = graphUserRepository ?? throw new ArgumentNullException(nameof(graphUserRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(AzureUserCreatorFunction))]
        public async Task<List<GraphProfileInformation>> AddUsersAsync([ActivityTrigger] AzureUserCreatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AzureUserCreatorFunction)} function started" });

            var newUsers = request.PersonnelNumbers.Select(x => new User
            {
                DisplayName = $"{request.TenantInformation.EmailPrefix} {x}",
                AccountEnabled = true,
                PasswordProfile = new PasswordProfile { Password = PasswordGenerator.GeneratePassword() },
                MailNickname = $"{request.TenantInformation.EmailPrefix}{x}",
                UsageLocation = request.TenantInformation.CountryCode,
                UserPrincipalName = $"{request.TenantInformation.EmailPrefix}{x}@{request.TenantInformation.TenantDomain}",
                OnPremisesImmutableId = x
            })
            .ToList();

            var newProfiles = await _graphUserRepository.AddUsersAsync(newUsers, null);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AzureUserCreatorFunction)} function completed" });

            return newProfiles;
        }
    }
}
