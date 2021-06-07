// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using AzureUserReader.Requests;
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AzureUserReader.PersonnelNumberReader
{
    public class PersonnelNumberReaderFunction
    {
        private readonly IAzureUserReaderService _azureUserReaderService = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public PersonnelNumberReaderFunction(IAzureUserReaderService azureUserReaderService, ILoggingRepository loggingRepository)
        {
            _azureUserReaderService = azureUserReaderService ?? throw new ArgumentNullException(nameof(azureUserReaderService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(PersonnelNumberReaderFunction))]
        public async Task<IList<string>> GetPersonnelNumbersAsync([ActivityTrigger] AzureUserReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PersonnelNumberReaderFunction)} function started" });

            var personnelNumbers = await _azureUserReaderService.GetPersonnelNumbersAsync(request.ContainerName, request.BlobPath);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PersonnelNumberReaderFunction)} function completed" });

            return personnelNumbers;
        }
    }
}