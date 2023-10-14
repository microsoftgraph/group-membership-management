// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;


namespace Hosts.NonProdService
{
    public class TenantUserCountFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUserRepository _graphUserRepository = null;

        public TenantUserCountFunction(ILoggingRepository loggingRepository, IGraphUserRepository graphUserRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUserRepository = graphUserRepository ?? throw new ArgumentNullException(nameof(graphUserRepository));
        }

        [FunctionName(nameof(TenantUserCountFunction))]
        public async Task<int?> GetTenantUsersAsync([ActivityTrigger] TenantUserCountRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TenantUserCountFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var userCount = await _graphUserRepository.GetUsersCountAsync(request.RunId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TenantUserCountFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return userCount;
        }
    }
}
