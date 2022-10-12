// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.AzureMaintenance;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
	public class AzureMaintenanceService : IAzureMaintenanceService
	{
		private readonly List<AzureMaintenance> _tablesToBackup = null;
		private readonly ILoggingRepository _loggingRepository = null;
		private readonly IAzureTableBackupRepository _azureTableBackupRepository = null;
		private readonly IAzureStorageBackupRepository _azureBlobBackupRepository = null;

		public AzureMaintenanceService(
			List<AzureMaintenance> tablesToBackup,
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
				if (!table.CleanupOnly)
				{
					await BackupTableAsync(table);
				}
			}
		}

		public async Task<List<IReviewAndDeleteRequest>> RetrieveBackupsAsync()
		{
			var requests = new List<IReviewAndDeleteRequest>();

			foreach (var table in _tablesToBackup)
			{
				var backupStorage = DetermineBackupStorage(table.BackupType);
				var backupEntities = await backupStorage.GetBackupsAsync(table);
				foreach(var backupEntity in backupEntities)
                {
					requests.Add(new ReviewAndDeleteRequest
					{
						TableName = backupEntity.Name,
						BackupSetting = table
					});
                }
			}

			return requests;
		}

		public async Task<bool> ReviewAndDeleteAsync(IAzureMaintenance backupSetting, string tableName)
		{
			var backupStorage = DetermineBackupStorage(backupSetting.BackupType);
			var shouldDelete = await backupStorage.VerifyDeleteBackupAsync(backupSetting, tableName);
			if (shouldDelete)
			{
				await backupStorage.DeleteBackupAsync(backupSetting, tableName);

				if (!backupSetting.CleanupOnly && backupSetting.BackupType.ToLowerInvariant() == "table")
				{
					await DeleteOldBackupTrackersAsync(backupSetting, new List<string> { tableName });
				}
			}

			return shouldDelete;
		}

		private async Task BackupTableAsync(IAzureMaintenance table)
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

		private async Task CompareBackupResults(IAzureMaintenance backupSettings, BackupResult currentBackup)
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

		private async Task DeleteOldBackupTrackersAsync(IAzureMaintenance backupSettings, List<string> deletedTables)
		{
			var keys = deletedTables.Select(x => (backupSettings.SourceTableName, x)).ToList();
			await _azureTableBackupRepository.DeleteBackupTrackersAsync(backupSettings, keys);
		}
	}
}
