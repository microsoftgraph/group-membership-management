// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;

namespace Hosts.AzureMaintenance
{
    public class TableBackupFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public TableBackupFunction(IAzureMaintenanceService azureMaintenanceService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [FunctionName(nameof(TableBackupFunction))]
        public async Task BackupTables([ActivityTrigger] object request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableBackupFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            await _azureMaintenanceService.RunTableBackupServiceAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableBackupFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
