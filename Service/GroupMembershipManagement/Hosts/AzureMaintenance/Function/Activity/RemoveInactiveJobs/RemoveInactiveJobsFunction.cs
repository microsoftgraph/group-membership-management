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
    public class RemoveInactiveJobsFunction
    {
        private readonly ILogger<RemoveInactiveJobsFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public RemoveInactiveJobsFunction(ILogger<RemoveInactiveJobsFunction> logger, IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(RemoveInactiveJobsFunction))]
        public async Task RemoveInactiveJobsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            _logger.FunctionStarted(nameof(RemoveInactiveJobsFunction));
            await _azureMaintenanceService.RemoveInactiveJobsAsync(syncJobs);
            _logger.FunctionCompleted(nameof(RemoveInactiveJobsFunction));
        }
    }
}