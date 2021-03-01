// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureTableBackup;
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
            if (!_tablesToBackup.Any())
            {
                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"No backup settings have been found." });
                return;
            }

            foreach (var table in _tablesToBackup)
            {
                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Starting backup for table: {table.SourceTableName}" });
                var entities = await _azureTableBackupRepository.GetEntitiesAsync(table);

                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Backing up {entities.Count} entites from table: {table.SourceTableName}" });
                var backupResult = await _azureTableBackupRepository.BackupEntitiesAsync(table, entities);

                await CompareBackupResults(table, backupResult);

                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Deleting old backups for table: {table.SourceTableName}" });
                var deletedTables = await DeleteOldBackupTablesAsync(table);
                await DeleteOldBackupTrackersAsync(table, deletedTables);
            }
        }

        private async Task<List<string>> DeleteOldBackupTablesAsync(IAzureTableBackup backupSettings)
        {
            var backupTables = await _azureTableBackupRepository.GetBackupTablesAsync(backupSettings);
            var cutOffDate = new DateTimeOffset(DateTime.UtcNow).AddDays(-backupSettings.DeleteAfterDays);
            var deletedTables = new List<string>();

            foreach (var table in backupTables)
            {
                if (!table.TimeStamp.HasValue || table.TimeStamp < cutOffDate)
                {
                    await _azureTableBackupRepository.DeleteBackupTableAsync(backupSettings, table.TableName);
                    deletedTables.Add(table.TableName);
                }
            }

            return deletedTables;
        }

        private async Task CompareBackupResults(IAzureTableBackup backupSettings, BackupResult currentBackup)
        {
            var previousBackupTracker = await _azureTableBackupRepository.GetLastestBackupResultTrackerAsync(backupSettings);
            await _azureTableBackupRepository.AddBackupResultTrackerAsync(backupSettings, currentBackup);

            if (previousBackupTracker == null)
            {
                return;
            }

            var delta = currentBackup.RowCount - previousBackupTracker.RowCount;
            var message = delta == 0 ? " same number of" : ((delta > 0) ? " more" : " less");
            await _loggingRepository.LogMessageAsync(
                new Entities.LogMessage
                {
                    Message = $"Current backup for {backupSettings.SourceTableName} has {delta}{message} rows than previous backup",
                    DynamicProperties =
                    {
                        { "status", "Delta" },
                        { "rowCount", delta.ToString() }
                    }
                });
        }

        private async Task DeleteOldBackupTrackersAsync(IAzureTableBackup backupSettings, List<string> deletedTables)
        {
            var keys = deletedTables.Select(x => (backupSettings.SourceTableName, x)).ToList();
            await _azureTableBackupRepository.DeleteBackupTrackersAsync(backupSettings, keys);
        }
    }
}
