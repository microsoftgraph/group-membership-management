// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.AzureMaintenance
{
    public class EmailSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public EmailSenderFunction(ILoggingRepository loggingRepository,
            IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
                
                await _azureMaintenanceService.SendEmailAsync(request.SyncJob, request.NotificationType);
                
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}