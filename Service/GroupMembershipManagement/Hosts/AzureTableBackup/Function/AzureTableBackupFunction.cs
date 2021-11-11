// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Entities;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using Services.Contracts;

namespace Hosts.AzureTableBackup
{
    public class AzureTableBackupFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureTableBackupService _azureTableBackupService = null;
        public AzureTableBackupFunction(IAzureTableBackupService azureTableBackupService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureTableBackupService = azureTableBackupService ?? throw new ArgumentNullException(nameof(azureTableBackupService));
        }

        [FunctionName("AzureTableBackup")]
        public async Task Run([TimerTrigger("%backupTriggerSchedule%")] TimerInfo myTimer)
        {
            _loggingRepository.SyncJobProperties = new Dictionary<string, string> { { "runId", Guid.NewGuid().ToString() } };
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"AzureTableBackup function started at: {DateTime.UtcNow}" });
            await _azureTableBackupService.RunBackupServiceAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"AzureTableBackup function completed at: {DateTime.UtcNow}" });
        }
    }
}
