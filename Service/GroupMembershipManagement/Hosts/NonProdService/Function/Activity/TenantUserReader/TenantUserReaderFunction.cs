// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class TenantUserReaderFunction
    {
        private readonly ILogger<TenantUserReaderFunction> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public TenantUserReaderFunction(ILogger<TenantUserReaderFunction> logger, IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(TenantUserReaderFunction))]
        public async Task<List<AzureADUser>> GetTenantUsersAsync([ActivityTrigger] TenantUserReaderRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(TenantUserReaderFunction));

                var users = await _graphGroupRepository.GetTenantUsers(request.MinimunTenantUserCount);

                if(users.Count< request.MinimunTenantUserCount)
                {
                    return null;
                }

                _logger.FunctionCompleted(nameof(TenantUserReaderFunction));

                return users;
            }
        }
    }
}
