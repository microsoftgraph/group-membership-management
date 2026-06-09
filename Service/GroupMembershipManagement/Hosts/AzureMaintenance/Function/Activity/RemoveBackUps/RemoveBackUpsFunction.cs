// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class RemoveBackUpsFunction
    {
        private readonly ILogger<RemoveBackUpsFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public RemoveBackUpsFunction(ILogger<RemoveBackUpsFunction> logger, IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(RemoveBackUpsFunction))]
        public async Task<int> RemoveBackUpsAsync([ActivityTrigger] object obj)
        {
            _logger.FunctionStarted(nameof(RemoveBackUpsFunction));
            int countOfRemovedJobs = await _azureMaintenanceService.RemoveBackupsAsync();
            _logger.FunctionCompleted(nameof(RemoveBackUpsFunction));
            return countOfRemovedJobs;
        }
    }
}