// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class AzureTableBackupService : IAzureTableBackupService
    {
        private readonly List<IAzureTableBackup> _tablesToBackup = null;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureTableBackupRepository _azureTableBackupRepository = null;

        public AzureTableBackupService(List<IAzureTableBackup> tablesToBackup, ILoggingRepository loggingRepository, IAzureTableBackupRepository azureTableBackupRepository)
        {
            _tablesToBackup = tablesToBackup;
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureTableBackupRepository = azureTableBackupRepository ?? throw new ArgumentNullException(nameof(azureTableBackupRepository));
        }

        public async Task BackupTablesAsync()
        {
            foreach (var table in _tablesToBackup)
            {
                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Starting backup for table: {table.SourceTableName}" });
                var entities = await _azureTableBackupRepository.GetEntitiesAsync(table);

                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Backing up {entities.Count} entites from table: {table.SourceTableName}" });
                await _azureTableBackupRepository.BackupEntitiesAsync(table, entities);

                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Deleting old backups for table: {table.SourceTableName}" });
                await DeleteOldBackupTablesAsync(table);
            }
        }

        private async Task DeleteOldBackupTablesAsync(IAzureTableBackup backupSettings)
        {
            var backupTables = await _azureTableBackupRepository.GetBackupTablesAsync(backupSettings);
            var cutOffDate = new DateTimeOffset(DateTime.UtcNow).AddDays(-backupSettings.DeleteAfterDays);
            var query = new TableQuery<DynamicTableEntity>();

            foreach (var table in backupTables)
            {
                var message = table.ExecuteQuery(query).FirstOrDefault();

                if (message == null || message.Timestamp < cutOffDate)
                    await _azureTableBackupRepository.DeleteBackupTableAsync(backupSettings, table.Name);
            }
        }
    }
}
