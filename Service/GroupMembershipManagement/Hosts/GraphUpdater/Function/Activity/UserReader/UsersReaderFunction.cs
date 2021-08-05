// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class UsersReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _usersReaderService;

        public UsersReaderFunction(ILoggingRepository loggingRepository, IGraphUpdaterService usersReaderService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _usersReaderService = usersReaderService ?? throw new ArgumentNullException(nameof(usersReaderService));
        }

        [FunctionName(nameof(UsersReaderFunction))]
        public async Task<UsersPageResponse> GetUsersAsync([ActivityTrigger] UsersReaderRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderFunction)} function started", RunId = request.RunId });
            var response = await _usersReaderService.GetFirstMembersPageAsync(request.GroupId, request.RunId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderFunction)} function completed", RunId = request.RunId });
            return response;
        }
    }
}
