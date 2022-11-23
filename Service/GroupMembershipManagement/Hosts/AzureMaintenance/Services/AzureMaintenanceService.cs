// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.AzureMaintenance;
using Repositories.Contracts;
using Repositories.Contracts.AzureMaintenance;
using Services.Contracts;
using Services.Entities;
using Services.Entities.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class AzureMaintenanceService : IAzureMaintenanceService
	{
		private readonly ILoggingRepository _loggingRepository = null;
		private readonly IAzureTableBackupRepository _azureTableBackupRepository = null;
		private readonly IAzureStorageBackupRepository _azureBlobBackupRepository = null;

		public AzureMaintenanceService(
			ILoggingRepository loggingRepository,
			IAzureTableBackupRepository azureTableBackupRepository,
			IAzureStorageBackupRepository azureBlobBackupRepository)
		{
			_loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_azureTableBackupRepository = azureTableBackupRepository ?? throw new ArgumentNullException(nameof(azureTableBackupRepository));
			_azureBlobBackupRepository = azureBlobBackupRepository ?? throw new ArgumentNullException(nameof(azureBlobBackupRepository));
		}

		public async Task RunBackupServiceAsync(IAzureMaintenanceJob maintenanceJob)
        {
			if (maintenanceJob.SourceStorageSetting.StorageType == StorageType.table)
		        await BackupTableAsync(maintenanceJob);
        }

		public async Task<List<IReviewAndDeleteRequest>> RetrieveBackupsAsync(IAzureMaintenanceJob maintenanceJob)
		{
			var requests = new List<IReviewAndDeleteRequest>();

			var backupStorage = DetermineBackupStorage(maintenanceJob.SourceStorageSetting.StorageType);
			var backupEntities = await backupStorage.GetBackupsAsync(maintenanceJob);
			foreach(var backupEntity in backupEntities)
            {
				requests.Add(new ReviewAndDeleteRequest
				{
					TargetName = backupEntity.Name,
					MaintenanceSetting = new AzureMaintenanceJob(maintenanceJob),
				});
			}

			return requests;
		}

		public async Task<bool> ReviewAndDeleteAsync(IAzureMaintenanceJob maintenanceJob, string targetName)
		{
			var backupStorage = DetermineBackupStorage(maintenanceJob.SourceStorageSetting.StorageType);
			var shouldDelete = await backupStorage.VerifyCleanupAsync(maintenanceJob, targetName);
			if (shouldDelete)
			{
				await backupStorage.CleanupAsync(maintenanceJob, targetName);

				if (maintenanceJob.Backup && maintenanceJob.SourceStorageSetting.StorageType == StorageType.table)
				{
					await DeleteOldBackupTrackersAsync(new AzureMaintenanceJob(maintenanceJob), new List<string> { targetName });
				}
			}

			return shouldDelete;
		}

		private async Task BackupTableAsync(IAzureMaintenanceJob maintenanceJob)
		{
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Starting backup maintenance for table: {maintenanceJob.SourceStorageSetting.TargetName}" });
			var entities = await _azureTableBackupRepository.GetEntitiesAsync(maintenanceJob);

			if (entities == null)
				return;

			// Currently, this will only support backups to the same storage type as the source, so table to table right now
			IAzureStorageBackupRepository backUpTo = DetermineBackupStorage(maintenanceJob.DestinationStorageSetting.StorageType);
			if (backUpTo == null)
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"BackupType must be 'table' or 'blob'. Was {maintenanceJob.SourceStorageSetting.StorageType}. Not backing up {maintenanceJob.SourceStorageSetting.TargetName}." });
				return;
			}

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Backing up {entities.Count} entites from table {maintenanceJob.SourceStorageSetting.TargetName} to {maintenanceJob.SourceStorageSetting.StorageType} storage." });
			var backupResult = await backUpTo.BackupEntitiesAsync(maintenanceJob, entities);

			await CompareBackupResults(maintenanceJob, backupResult);
		}

		private IAzureStorageBackupRepository DetermineBackupStorage(StorageType backUpTo)
		{
			switch (backUpTo)
			{
				case StorageType.table:
					return _azureTableBackupRepository;
				case StorageType.blob:
					return _azureBlobBackupRepository;
				default:
					return null;
			}

		}

		private async Task CompareBackupResults(IAzureMaintenanceJob backupSettings, BackupResult currentBackup)
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
					Message = $"Current backup for {backupSettings.SourceStorageSetting.TargetName} has {delta}{message} rows than previous backup",
					DynamicProperties =
					{
						{ "status", "Delta" },
						{ "rowCount", delta.ToString() }
					}
				});
		}

		private async Task DeleteOldBackupTrackersAsync(IAzureMaintenanceJob backupSettings, List<string> deletedTables)
		{
			var keys = deletedTables.Select(x => (backupSettings.SourceStorageSetting.TargetName, x)).ToList();
			await _azureTableBackupRepository.DeleteBackupTrackersAsync(backupSettings, keys);
		}
	}
}
