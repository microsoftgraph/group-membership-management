// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class DeltaUsersReaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public DeltaUsersReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository;
            _calculator = calculator;
        }

        [FunctionName(nameof(DeltaUsersReaderFunction))]
        public async Task<(List<AzureADUser> usersToAdd,
                           List<AzureADUser> usersToRemove,
                           string nextPageUrl,
                           string deltaUrl,
                           IGroupDeltaCollectionPage response)> GetDeltaUsersAsync([ActivityTrigger] DeltaUsersReaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUsersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var response = await _calculator.GetFirstDeltaUsersPageAsync(request.DeltaLink);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUsersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return response;
            
        }
    }
}