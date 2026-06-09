// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class ReadSyncJobsFunction
    {
        private readonly ILogger<ReadSyncJobsFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public ReadSyncJobsFunction(ILogger<ReadSyncJobsFunction> logger, IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(ReadSyncJobsFunction))]
        public async Task<List<SyncJob>> GetSyncJobsAsync([ActivityTrigger] object obj)
        {
            _logger.FunctionStarted(nameof(ReadSyncJobsFunction));
            var jobs = await _azureMaintenanceService.GetSyncJobsAsync();
            _logger.FunctionCompleted(nameof(ReadSyncJobsFunction));
            return jobs;
        }
    }
}