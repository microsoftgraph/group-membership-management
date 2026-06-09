// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class JobsFilterFunction
    {
        private readonly ILogger<JobsFilterFunction> _logger;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService;

        public JobsFilterFunction(ILogger<JobsFilterFunction> logger, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [Function(nameof(JobsFilterFunction))]
        public Task<List<Guid>> GetJobsAsync([ActivityTrigger] JobsFilterRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });
            _logger.FunctionStarted(nameof(JobsFilterFunction));
            var filteredJobs = _groupOwnershipObtainerService.FilterSyncJobsBySourceTypes(request.RequestedSources, request.SyncJobs);
            _logger.FunctionCompleted(nameof(JobsFilterFunction));

            return Task.FromResult(filteredJobs.ToList());
        }
    }
}
