// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureMaintenance;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.AzureMaintenance;
using Repositories.Contracts.InjectConfig;
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
        private const string CustomerPausedJobEmailSubject = "CustomerPausedJobEmailSubject";
        private const string CustomerPausedJobEmailBody = "CustomerPausedJobEmailBody";

        private readonly ILoggingRepository _loggingRepository = null;
		private readonly IAzureTableBackupRepository _azureTableBackupRepository = null;
		private readonly IAzureStorageBackupRepository _azureBlobBackupRepository = null;
        private readonly ISyncJobRepository _syncJobRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly IMailRepository _mailRepository = null;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig = null;

        public AzureMaintenanceService(
			ILoggingRepository loggingRepository,
			IAzureTableBackupRepository azureTableBackupRepository,
            IAzureStorageBackupRepository azureBlobBackupRepository,
            ISyncJobRepository syncJobRepository,
            IGraphGroupRepository graphGroupRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IMailRepository mailRepository,
			IHandleInactiveJobsConfig handleInactiveJobsConfig)
        {
			_loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_azureTableBackupRepository = azureTableBackupRepository ?? throw new ArgumentNullException(nameof(azureTableBackupRepository));
			_azureBlobBackupRepository = azureBlobBackupRepository ?? throw new ArgumentNullException(nameof(azureBlobBackupRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _handleInactiveJobsConfig = handleInactiveJobsConfig ?? throw new ArgumentNullException(nameof(handleInactiveJobsConfig));
        }

		public async Task RunBackupServiceAsync(IAzureMaintenanceJob maintenanceJob)
        {
			if (maintenanceJob.SourceStorageSetting.StorageType == StorageType.Table)
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

				if (maintenanceJob.Backup && maintenanceJob.SourceStorageSetting.StorageType == StorageType.Table)
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
				case StorageType.Table:
					return _azureTableBackupRepository;
				case StorageType.Blob:
					return _azureBlobBackupRepository;
				default:
					return null;
			}

		}

		private async Task CompareBackupResults(IAzureMaintenanceJob backupSettings, BackupResult currentBackup)
		{
			var previousBackupTracker = await _azureTableBackupRepository.GetLatestBackupResultTrackerAsync(backupSettings);
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

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            var allJobs = new List<SyncJob>();
            var jobs = _syncJobRepository.GetSpecificSyncJobsAsync();
			if (jobs == null) { return allJobs; }
            await foreach (var job in jobs)
            {
                allJobs.Add(job);
            }
            return allJobs;
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task SendEmailAsync(SyncJob job, string groupName)
        {
            if (job != null)
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(job.TargetOfficeGroupId);
                var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
                var message = new EmailMessage
                {
                    Subject = CustomerPausedJobEmailSubject,
                    Content = CustomerPausedJobEmailBody,
                    SenderAddress = _emailSenderAndRecipients.SenderAddress,
                    SenderPassword = _emailSenderAndRecipients.SenderPassword,
                    ToEmailAddresses = ownerEmails,
                    CcEmailAddresses = _emailSenderAndRecipients.SupportEmailAddresses,
                    AdditionalContentParams = new[]
                    {
                        groupName,
                        job.TargetOfficeGroupId.ToString(),
                        DateTime.UtcNow.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforeDeletion-5).ToString()
                    }
                };

                await _mailRepository.SendMailAsync(message, job.RunId);
            }
        }

        public async Task<int> BackupInactiveJobsAsync(List<SyncJob> syncJobs)
        {
            if (syncJobs.Count <= 0) return 0;
            return await _azureTableBackupRepository.BackupInactiveJobsAsync(syncJobs);
        }

        public async Task<List<string>> RemoveBackupsAsync()
        {
            var backupTables = await _azureTableBackupRepository.GetInactiveBackupsAsync();
            var cutOffDate = DateTime.UtcNow.AddDays(-_handleInactiveJobsConfig.NumberOfDaysBeforeDeletion);
            var deletedTables = new List<string>();

            foreach (var table in backupTables)
            {
                if (table.CreatedDate < cutOffDate)
                {
                    await _azureTableBackupRepository.DeleteBackupTableAsync(table.TableName);
                    deletedTables.Add(table.TableName);
                }
            }

            return deletedTables;
        }

        public async Task RemoveInactiveJobsAsync(IEnumerable<SyncJob> jobs)
        {
            await _syncJobRepository.DeleteSyncJobsAsync(jobs);
        }
    }
}
