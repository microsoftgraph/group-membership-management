// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class EnsureGroupOwnershipFunction
    {
        private readonly ILogger<EnsureGroupOwnershipFunction> _logger;
        private readonly INonProdService _nonProdService;

        public EnsureGroupOwnershipFunction(ILogger<EnsureGroupOwnershipFunction> logger, INonProdService nonProdService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _nonProdService = nonProdService ?? throw new ArgumentNullException(nameof(nonProdService));
        }

        [Function(nameof(EnsureGroupOwnershipFunction))]
        public async Task<GroupOwnershipResult> RunAsync([ActivityTrigger] EnsureGroupOwnershipRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.EnsureOwnershipStarted(request.ManagedGroupIds.Count);

                var result = await _nonProdService.EnsureGroupOwnershipAsync(request.ManagedGroupIds, request.OwnerAppId, request.RunId);

                _logger.EnsureOwnershipCompleted(result.GroupsAlreadyOwned, result.GroupsNewlyOwned, result.GroupsFailed);

                return result;
            }
        }
    }
}
