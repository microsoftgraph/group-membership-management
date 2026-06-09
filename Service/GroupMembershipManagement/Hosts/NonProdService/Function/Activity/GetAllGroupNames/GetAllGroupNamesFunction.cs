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
    public class GetAllGroupNamesFunction
    {
        private readonly ILogger<GetAllGroupNamesFunction> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GetAllGroupNamesFunction(
            ILogger<GetAllGroupNamesFunction> logger,
            IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(GetAllGroupNamesFunction))]
        public async Task<GetAllGroupNamesResponse> GetAllGroupNamesAsync([ActivityTrigger] GetAllGroupNamesRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(GetAllGroupNamesFunction));

                var groups = await _graphGroupRepository.GetAllGroupNamesAsync();

                _logger.FunctionCompleted(nameof(GetAllGroupNamesFunction));

                return new GetAllGroupNamesResponse
                {
                    Groups = groups
                };
            }
        }
    }
}