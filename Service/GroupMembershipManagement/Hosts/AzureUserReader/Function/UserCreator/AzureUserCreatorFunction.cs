// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class AzureUserCreatorFunction
    {
        private readonly IGraphUserRepository _graphUserRepository;
        private readonly ILogger<AzureUserCreatorFunction> _logger;

        public AzureUserCreatorFunction(IGraphUserRepository graphUserRepository, ILogger<AzureUserCreatorFunction> logger)
        {
            _graphUserRepository = graphUserRepository ?? throw new ArgumentNullException(nameof(graphUserRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(AzureUserCreatorFunction))]
        public async Task<List<GraphProfileInformation>> AddUsersAsync([ActivityTrigger] AzureUserCreatorRequest request)
        {
            _logger.FunctionStarted(nameof(AzureUserCreatorFunction));

            if (request == null || request.TenantInformation == null)
            {
                _logger.RequestOrTenantInfoNull(nameof(AzureUserCreatorFunction));
                throw new ArgumentNullException(request == null ? nameof(request) : nameof(request.TenantInformation));
            }

            var newUsers = request.PersonnelNumbers
                .Where(x => long.TryParse(x, out _))
                .Select(x => new GraphUser
                {
                    DisplayName = $"{request.TenantInformation.EmailPrefix} {x}",
                    AccountEnabled = true,
                    Password = PasswordGenerator.GeneratePassword(),
                    MailNickname = $"{request.TenantInformation.EmailPrefix}{x}",
                    UsageLocation = request.TenantInformation.CountryCode,
                    UserPrincipalName = $"{request.TenantInformation.EmailPrefix}{x}@{request.TenantInformation.TenantDomain}",
                    OnPremisesImmutableId = x
                })
                .ToList();

            var newProfiles = await _graphUserRepository.AddUsersAsync(newUsers, null);

            _logger.FunctionCompleted(nameof(AzureUserCreatorFunction));

            return newProfiles;
        }
    }
}
