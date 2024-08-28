// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class LoggerFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public LoggerFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(LoggerFunction))]
        public async Task LogMessageAsync([ActivityTrigger] LoggerRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = request.Message, RunId = request.RunId },request.Verbosity);
        }
    }
}
