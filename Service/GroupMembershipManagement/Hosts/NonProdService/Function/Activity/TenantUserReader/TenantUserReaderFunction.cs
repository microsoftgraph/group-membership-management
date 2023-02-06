// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Models.Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class TenantUserReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public TenantUserReaderFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(TenantUserReaderFunction))]
        public async Task<List<AzureADUser>> GetTenantUsersAsync([ActivityTrigger] TenantUserReaderRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TenantUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var users = await _graphGroupRepository.GetTenantUsers(request.MinimunTenantUserCount);

            if(users.Count< request.MinimunTenantUserCount)
            {
                return null;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TenantUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return users;
        }
    }
}
