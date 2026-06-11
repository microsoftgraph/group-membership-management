// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class RemoveInactiveJobsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public RemoveInactiveJobsFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(RemoveInactiveJobsFunction))]
        public async Task RemoveInactiveJobsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RemoveInactiveJobsFunction)} function started" }, VerbosityLevel.DEBUG);
            await _azureMaintenanceService.RemoveInactiveJobsAsync(syncJobs);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RemoveInactiveJobsFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}