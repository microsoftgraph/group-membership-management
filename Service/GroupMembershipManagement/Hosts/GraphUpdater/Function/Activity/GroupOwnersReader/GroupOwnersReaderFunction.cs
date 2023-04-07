// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupOwnersReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GroupOwnersReaderFunction(ILoggingRepository loggingRepository, IGraphUpdaterService graphUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [FunctionName(nameof(GroupOwnersReaderFunction))]
        public async Task<List<User>> GetGroupOwnersAsync([ActivityTrigger] GroupOwnersReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupOwnersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _graphUpdaterService.RunId = request.RunId;
            var owners = await _graphUpdaterService.GetGroupOwnersAsync(request.GroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupOwnersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return owners;
        }
    }
}