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
    public class PurgingEmailSenderFunction
    {
        private readonly ILogger<PurgingEmailSenderFunction> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public PurgingEmailSenderFunction(ILogger<PurgingEmailSenderFunction> logger,
            IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(PurgingEmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] PurgingEmailSenderRequest request)
        {
            if (request.SyncJob != null)
            {
                using (_logger.BeginRunIdScope(request.RunId))
                {
                    _logger.FunctionStarted(nameof(PurgingEmailSenderFunction));
                    
                    await _azureMaintenanceService.SendPurgingEmailAsync(request.SyncJob, request.NotificationType);
                    
                    _logger.FunctionCompleted(nameof(PurgingEmailSenderFunction));
                }
            }
        }
    }
}