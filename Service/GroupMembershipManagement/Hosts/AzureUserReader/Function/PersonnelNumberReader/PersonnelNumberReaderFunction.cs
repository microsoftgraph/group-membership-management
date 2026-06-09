// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class PersonnelNumberReaderFunction
    {
        private readonly IAzureUserReaderService _azureUserReaderService;
        private readonly ILogger<PersonnelNumberReaderFunction> _logger;

        public PersonnelNumberReaderFunction(IAzureUserReaderService azureUserReaderService, ILogger<PersonnelNumberReaderFunction> logger)
        {
            _azureUserReaderService = azureUserReaderService ?? throw new ArgumentNullException(nameof(azureUserReaderService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(PersonnelNumberReaderFunction))]
        public async Task<IList<string>> GetPersonnelNumbersAsync([ActivityTrigger] AzureUserReaderRequest request)
        {
            _logger.FunctionStarted(nameof(PersonnelNumberReaderFunction));

            var personnelNumbers = await _azureUserReaderService.GetPersonnelNumbersAsync(request.ContainerName, request.BlobPath);

            _logger.FunctionCompleted(nameof(PersonnelNumberReaderFunction));

            return personnelNumbers;
        }
    }
}