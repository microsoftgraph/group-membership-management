// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;

namespace Hosts.AzureBackup
{
    public class TableBackupFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureBackupService _azureTableBackupService = null;
        public TableBackupFunction(IAzureBackupService azureTableBackupService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureTableBackupService = azureTableBackupService ?? throw new ArgumentNullException(nameof(azureTableBackupService));
        }

        [FunctionName(nameof(TableBackupFunction))]
        public async Task BackupTables([ActivityTrigger] object request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableBackupFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            await _azureTableBackupService.RunBackupServiceAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableBackupFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
