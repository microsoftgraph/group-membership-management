// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
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
            await _loggingRepository.LogMessageAsync(request.Message, request.Verbosity);
        }
    }
}
