// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class GetAllGroupNamesFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public GetAllGroupNamesFunction(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(GetAllGroupNamesFunction))]
        public async Task<GetAllGroupNamesResponse> GetAllGroupNamesAsync([ActivityTrigger] GetAllGroupNamesRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetAllGroupNamesFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var groupNames = await _graphGroupRepository.GetAllGroupNamesAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetAllGroupNamesFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return new GetAllGroupNamesResponse
            {
                GroupNames = groupNames
            };
        }
    }
}