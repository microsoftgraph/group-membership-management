// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AzureUserReader.UserReader
{
    public class AzureUserReaderFunction
    {
        private readonly IGraphUserRepository _graphUserRepository = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public AzureUserReaderFunction(IGraphUserRepository graphUserRepository, ILoggingRepository loggingRepository)
        {
            _graphUserRepository = graphUserRepository ?? throw new ArgumentNullException(nameof(graphUserRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(AzureUserReaderFunction))]
        public async Task<IList<GraphProfileInformation>> GetUsersAsync([ActivityTrigger] List<string> personnelNumbers)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AzureUserReaderFunction)} function started" });

            var users = await _graphUserRepository.GetAzureADObjectIdsAsync(personnelNumbers, null);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AzureUserReaderFunction)} function completed" });

            return users;
        }
    }
}
