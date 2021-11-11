// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
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
		private readonly IAzureStorageBackupRepository _azureBlobBackupRepository = null;

		public AzureTableBackupService(
			List<IAzureTableBackup> tablesToBackup,
			ILoggingRepository loggingRepository,
			IAzureTableBackupRepository azureTableBackupRepository,
			IAzureStorageBackupRepository azureBlobBackupRepository)
		{
			_tablesToBackup = tablesToBackup;
			_loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_azureTableBackupRepository = azureTableBackupRepository ?? throw new ArgumentNullException(nameof(azureTableBackupRepository));
			_azureBlobBackupRepository = azureBlobBackupRepository ?? throw new ArgumentNullException(nameof(azureBlobBackupRepository));
		}

		public async Task RunBackupServiceAsync()
		{
			if (!_tablesToBackup.Any())
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"No backup settings have been found." });
				return;
			}

			foreach (var table in _tablesToBackup)
			{
				if(table.CleanupOnly)
				{
					await DeleteOldBackupsAsync(table);
				}
				else
				{
					await BackupTableAsync(table);
				}
			}
		}

		private async Task BackupTableAsync(IAzureTableBackup table)
		{
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Starting backup maintenance for table: {table.SourceTableName}" });
			var entities = await _azureTableBackupRepository.GetEntitiesAsync(table);

			if (entities == null)
				return;

			// basically, the table storage gets used regardless, to read the source table
			// and to maintain the tracking table. this determines whether the backup data
			// is stored in table or blob storage.
			IAzureStorageBackupRepository backUpTo = DetermineBackupStorage(table.BackupType);
			if (backUpTo == null)
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"BackupType must be 'table' or 'blob'. Was {table.BackupType}. Not backing up {table.SourceTableName}." });
				return;
			}

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Backing up {entities.Count} entites from table {table.SourceTableName} to {table.BackupType} storage." });
			var backupResult = await backUpTo.BackupEntitiesAsync(table, entities);

			await CompareBackupResults(table, backupResult);

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting old backups for table: {table.SourceTableName}" });
			var deletedTables = await DeleteOldBackupsAsync(table);
			await DeleteOldBackupTrackersAsync(table, deletedTables);
		}

		private IAzureStorageBackupRepository DetermineBackupStorage(string backUpTo)
		{
			switch (backUpTo.ToLowerInvariant())
			{
				case "table":
					return _azureTableBackupRepository;
				case "blob":
					return _azureBlobBackupRepository;
				default:
					return null;
			}

		}

		private async Task<List<string>> DeleteOldBackupsAsync(IAzureTableBackup backupSettings)
		{
			// note that only backups of the current storage type will be deleted
			// if you switch from table to blob, this won't delete the table backups
			// unless you delete them manually or switch back to table storage.
			var backupStorage = DetermineBackupStorage(backupSettings.BackupType);
			var backupEntities = await backupStorage.GetBackupsAsync(backupSettings);
			var deletedEntities = await backupStorage.DeleteBackupsAsync(backupSettings, backupEntities);

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted {deletedEntities.Count} old backups for table: {backupSettings.SourceTableName}" });
			return deletedEntities;
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
				new LogMessage
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
