// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.AzureMaintenance
{
    public class ExpireNotificationsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public ExpireNotificationsFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(ExpireNotificationsFunction))]
        public async Task ExpireNotificationsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ExpireNotificationsFunction)} function started" }, VerbosityLevel.DEBUG);
            await _azureMaintenanceService.ExpireNotificationsAsync(syncJobs);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ExpireNotificationsFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}