// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class JobReaderFunction
    {
        private readonly ILogger<JobReaderFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public JobReaderFunction(ILogger<JobReaderFunction> logger, IGraphUpdaterService graphUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [Function(nameof(JobReaderFunction))]
        public async Task<SyncJob> GetSyncJobAsync([ActivityTrigger] JobReaderRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(JobReaderFunction));
            var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.SyncJob.Id);
            _logger.FunctionCompleted(nameof(JobReaderFunction));
            return syncJob;
        }
    }
}
