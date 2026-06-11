// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobReaderFunction
    {
        private readonly ILogger<JobReaderFunction> _logger;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;

        public JobReaderFunction(ILogger<JobReaderFunction> logger, IDatabaseSyncJobsRepository syncJobRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
        }

        [Function(nameof(JobReaderFunction))]
        public async Task<SyncJob> GetSyncJobAsync([ActivityTrigger] JobReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(JobReaderFunction));
                var syncJob = await _syncJobRepository.GetSyncJobAsync(request.JobId);
                _logger.FunctionCompleted(nameof(JobReaderFunction));
                return syncJob;
            }
        }
    }
}
