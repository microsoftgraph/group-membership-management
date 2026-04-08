// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class AzureUserReaderFunction
    {
        private readonly IGraphUserRepository _graphUserRepository;
        private readonly ILogger<AzureUserReaderFunction> _logger;

        public AzureUserReaderFunction(IGraphUserRepository graphUserRepository, ILogger<AzureUserReaderFunction> logger)
        {
            _graphUserRepository = graphUserRepository ?? throw new ArgumentNullException(nameof(graphUserRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(AzureUserReaderFunction))]
        public async Task<IList<GraphProfileInformation>> GetUsersAsync([ActivityTrigger] List<string> personnelNumbers)
        {
            _logger.FunctionStarted(nameof(AzureUserReaderFunction));

            var users = await _graphUserRepository.GetAzureADObjectIdsAsync(personnelNumbers, null);

            _logger.FunctionCompleted(nameof(AzureUserReaderFunction));

            return users;
        }
    }
}
