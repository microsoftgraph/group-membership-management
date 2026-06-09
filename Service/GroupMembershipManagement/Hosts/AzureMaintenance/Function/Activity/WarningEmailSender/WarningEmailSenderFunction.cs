// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models.AzureMaintenance;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class WarningEmailSenderFunction
    {
        private readonly ILogger<WarningEmailSenderFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public WarningEmailSenderFunction(ILogger<WarningEmailSenderFunction> logger,
            IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(WarningEmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] WarningEmailSenderRequest request)
        {
            if (request.SyncJob != null)
            {
                using (_logger.BeginRunIdScope(request.RunId))
                {
                    _logger.FunctionStarted(nameof(WarningEmailSenderFunction));
                    
                    await _azureMaintenanceService.SendWarningEmailAsync(request.SyncJob, request.NotificationType);
                    
                    _logger.FunctionCompleted(nameof(WarningEmailSenderFunction));
                }
            }
        }
    }
}