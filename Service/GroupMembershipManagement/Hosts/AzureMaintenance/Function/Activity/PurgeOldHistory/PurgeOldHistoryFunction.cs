// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class PurgeOldHistoryFunction
    {
        private readonly ILogger<PurgeOldHistoryFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public PurgeOldHistoryFunction(ILogger<PurgeOldHistoryFunction> logger, IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(PurgeOldHistoryFunction))]
        public async Task<int> PurgeOldHistoryAsync([ActivityTrigger] object obj)
        {
            _logger.FunctionStarted(nameof(PurgeOldHistoryFunction));
            int countOfDeletedRecords = await _azureMaintenanceService.PurgeOldHistoryAsync();
            _logger.FunctionCompleted(nameof(PurgeOldHistoryFunction));
            return countOfDeletedRecords;
        }
    }
}
