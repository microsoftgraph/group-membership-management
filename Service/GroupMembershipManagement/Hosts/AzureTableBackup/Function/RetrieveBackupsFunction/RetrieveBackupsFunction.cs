// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Services.Contracts;
using Repositories.Contracts;
using Services.Entities;

namespace Hosts.AzureTableBackup
{
    public class RetrieveBackupsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureTableBackupService _azureTableBackupService = null;
        public RetrieveBackupsFunction(IAzureTableBackupService azureTableBackupService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureTableBackupService = azureTableBackupService ?? throw new ArgumentNullException(nameof(azureTableBackupService));
        }

        [FunctionName(nameof(RetrieveBackupsFunction))]
        public async Task<List<ReviewAndDeleteRequest>> RetrieveBackups([ActivityTrigger] RetrieveBackupsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RetrieveBackupsFunction)} function started at: {DateTime.UtcNow}" });
            var backups = await _azureTableBackupService.RetrieveBackupsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RetrieveBackupsFunction)} function completed at: {DateTime.UtcNow}" });

            return backups.Select(e => new ReviewAndDeleteRequest() { TableName = e.TableName, BackupSetting = e.BackupSetting }).ToList();
        }
    }
}
