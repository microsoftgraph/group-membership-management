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
    public class ExpireNotificationsFunction
    {
        private readonly ILogger<ExpireNotificationsFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public ExpireNotificationsFunction(ILogger<ExpireNotificationsFunction> logger, IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(ExpireNotificationsFunction))]
        public async Task ExpireNotificationsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            _logger.FunctionStarted(nameof(ExpireNotificationsFunction));
            await _azureMaintenanceService.ExpireNotificationsAsync(syncJobs);
            _logger.FunctionCompleted(nameof(ExpireNotificationsFunction));
        }
    }
}