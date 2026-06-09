// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.AzureMaintenance;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class BackUpInactiveJobsFunction
    {
        private readonly ILogger<BackUpInactiveJobsFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public BackUpInactiveJobsFunction(ILogger<BackUpInactiveJobsFunction> logger, IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(BackUpInactiveJobsFunction))]
        public async Task<List<PurgedSyncJob>> BackupInactiveJobsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            _logger.FunctionStarted(nameof(BackUpInactiveJobsFunction));
            var backUpJobs = await _azureMaintenanceService.BackupInactiveJobsAsync(syncJobs);
            _logger.FunctionCompleted(nameof(BackUpInactiveJobsFunction));
            return backUpJobs;
        }
    }
}