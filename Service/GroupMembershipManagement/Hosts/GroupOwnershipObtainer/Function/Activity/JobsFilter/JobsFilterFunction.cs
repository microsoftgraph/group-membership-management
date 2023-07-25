// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class JobsFilterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService;

        public JobsFilterFunction(ILoggingRepository loggingRepository, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [FunctionName(nameof(JobsFilterFunction))]
        public async Task<List<Guid>> GetJobsAsync([ActivityTrigger] JobsFilterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupOwnersFunction)} function started at: {DateTime.UtcNow}", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var filteredJobs = _groupOwnershipObtainerService.FilterSyncJobsBySourceTypes(request.RequestedSources, request.SyncJobs);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupOwnersFunction)} function completed at: {DateTime.UtcNow}", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return filteredJobs.ToList();
        }
    }
}
