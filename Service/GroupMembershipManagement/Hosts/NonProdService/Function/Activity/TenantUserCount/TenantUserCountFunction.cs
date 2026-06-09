// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;


namespace Hosts.NonProdService
{
    public class TenantUserCountFunction
    {
        private readonly ILogger<TenantUserCountFunction> _logger;
        private readonly IGraphUserRepository _graphUserRepository = null;

        public TenantUserCountFunction(ILogger<TenantUserCountFunction> logger, IGraphUserRepository graphUserRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUserRepository = graphUserRepository ?? throw new ArgumentNullException(nameof(graphUserRepository));
        }

        [Function(nameof(TenantUserCountFunction))]
        public async Task<int?> GetTenantUsersAsync([ActivityTrigger] TenantUserCountRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(TenantUserCountFunction));

                var userCount = await _graphUserRepository.GetUsersCountAsync(request.RunId);

                _logger.FunctionCompleted(nameof(TenantUserCountFunction));

                return userCount;
            }
        }
    }
}
