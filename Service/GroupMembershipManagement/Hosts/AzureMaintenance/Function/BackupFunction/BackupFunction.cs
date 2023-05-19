// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using Services.Entities;

namespace Hosts.AzureMaintenance
{
    public class BackupFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public BackupFunction(IAzureMaintenanceService azureMaintenanceService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [FunctionName(nameof(BackupFunction))]
        public async Task BackupTables([ActivityTrigger] AzureMaintenanceJob maintenanceJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BackupFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            await _azureMaintenanceService.RunBackupServiceAsync(maintenanceJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BackupFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
