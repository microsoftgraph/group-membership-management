// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.GroupMembershipObtainer
{
    public class DestinationNameReaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator = null;
        public DestinationNameReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator)); ;
        }

        [Function(nameof(DestinationNameReaderFunction))]
        public async Task<string> GetDestinationNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationNameReaderFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            if (syncJob == null)
                return null;
            
            _calculator.RunId = syncJob.RunId ?? Guid.Empty;
            var destinationName = await _calculator.GetDestinationNameAsync(syncJob);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationNameReaderFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            return destinationName;
        }
    }
}