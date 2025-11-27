// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class PurgingEmailSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public PurgingEmailSenderFunction(ILoggingRepository loggingRepository,
            IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(PurgingEmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] PurgingEmailSenderRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PurgingEmailSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
                
                await _azureMaintenanceService.SendPurgingEmailAsync(request.SyncJob, request.NotificationType);
                
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PurgingEmailSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}