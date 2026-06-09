// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.EntityFramework;
using System;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class LoadTestingSyncJobRetrieverFunction
    {
        private readonly ILogger<LoadTestingSyncJobRetrieverFunction> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository = null;

        public LoadTestingSyncJobRetrieverFunction(ILogger<LoadTestingSyncJobRetrieverFunction> logger, IDatabaseSyncJobsRepository databaseSyncJobsRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        [Function(nameof(LoadTestingSyncJobRetrieverFunction))]
        public async Task<LoadTestingSyncJobRetrieverResponse> GenerateGroup([ActivityTrigger] LoadTestingSyncJobRetrieverRequest request)
        {
            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(LoadTestingSyncJobRetrieverFunction));

                var syncJobs = await _databaseSyncJobsRepository.GetSyncJobsAsync();

                _logger.FunctionCompleted(nameof(LoadTestingSyncJobRetrieverFunction));

                return new LoadTestingSyncJobRetrieverResponse
                {
                    SyncJobs = syncJobs
                };
            }
        }
    }
}
