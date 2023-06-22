// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

        [FunctionName(nameof(ExpireNotificationsFunction))]
        public async Task ExpireNotificationsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ExpireNotificationsFunction)} function started" }, VerbosityLevel.DEBUG);
            await _azureMaintenanceService.ExpireNotificationsAsync(syncJobs);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ExpireNotificationsFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}