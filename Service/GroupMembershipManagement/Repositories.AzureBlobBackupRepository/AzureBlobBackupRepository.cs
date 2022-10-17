// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Storage.Blobs;
using CsvHelper;
using Entities;
using Entities.AzureMaintenance;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts;
using Repositories.Contracts.AzureMaintenance;
using Services.Entities.Contracts;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.AzureBlobBackupRepository
{
	public class AzureBlobBackupRepository : IAzureStorageBackupRepository
	{
		private readonly ILoggingRepository _loggingRepository;

		private const string BACKUP_PREFIX = "Backup";
		private const string BACKUP_DATE_FORMAT = "yyyyMMddHHmmss";

		public AzureBlobBackupRepository(ILoggingRepository loggingRepository)
		{
			_loggingRepository = loggingRepository;
		}

		public async Task<BackupResult> BackupEntitiesAsync(IAzureMaintenanceJob maintenanceJob, List<DynamicTableEntity> entities)
        {

            if (!entities.Any())
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Nothing to back up for {maintenanceJob.SourceStorageSetting.TargetName}. Skipping." });
			}

            BlobServiceClient blobServiceClient = new BlobServiceClient(maintenanceJob.DestinationStorageSetting.StorageConnectionString);
			string containerName = GetContainerName(maintenanceJob);
			var blobName = $"{BACKUP_PREFIX}{maintenanceJob.DestinationStorageSetting.TargetName}{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}.csv";

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Backing up data to blob: {blobName} started",
                    DynamicProperties = {
						{ "status", "Started" },
						{ "rowCount", entities.Count.ToString() } }
                });

			var blobClient = blobServiceClient.GetBlobContainerClient(containerName);
			await blobClient.CreateIfNotExistsAsync();

			await blobClient.UploadBlobAsync(blobName, new BinaryData(SerializeEntities(entities)));

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Backing up data to blob: {blobName} completed",
                    DynamicProperties = {
						{ "status", "Completed" },
						{ "rowCount", entities.Count.ToString() } }
                });

			return new BackupResult(blobName, "blob", entities.Count);
		}

		private static string GetContainerName(IAzureMaintenanceJob backupSettings)
		{
			return $"{BACKUP_PREFIX}{backupSettings.DestinationStorageSetting.TargetName}".ToLowerInvariant();
		}

		private string SerializeEntities(List<DynamicTableEntity> entities)
		{
			var keys = entities[0].Properties.Keys;
			using var writer = new StringWriter();
			using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
			{
				csv.WriteRecords(entities.Select(TableEntityToDynamic));
				return writer.ToString();
			}
		}

		private dynamic TableEntityToDynamic(DynamicTableEntity entity)
		{
			// basically, this is needed because CSVHelper doesn't like dictionaries or DynamicTableEntries, but it does like ExpandoObjects
			// https://joshclose.github.io/CsvHelper/examples/writing/write-dynamic-objects/

			dynamic toReturn = new ExpandoObject();
			toReturn.RowKey = entity.RowKey;
			toReturn.PartitionKey = entity.PartitionKey;
			toReturn.Timestamp = entity.Timestamp;
			foreach (var kvp in entity.Properties)
			{
				((IDictionary<string, object>)toReturn)[kvp.Key] = kvp.Value.PropertyAsObject;
			}

			return toReturn;
		}
		
		public async Task<List<BackupEntity>> GetBackupsAsync(IAzureMaintenanceJob maintenanceJob)
		{
            var blobContainerClient = await GetContainerClient(maintenanceJob);
			if(blobContainerClient == null)
            {
				return new List<BackupEntity>();
            }

			var allBlobs = blobContainerClient.GetBlobsAsync().AsPages();

			// Will create a list of BackupEntity instances where each one has different blob file path name
			var backupEntities = new List<BackupEntity>();
			backupEntities.Add(new BackupEntity(GetContainerName(maintenanceJob), "blob"));
			return backupEntities;
		}

		public async Task<bool> VerifyCleanupAsync(IAzureMaintenanceJob maintenanceJob, string blobName)
		{
            var cutOffDate = DateTime.UtcNow.AddDays(-maintenanceJob.DeleteAfterDays);

			var blobContainerClient = await GetContainerClient(maintenanceJob);
			var blobClient = blobContainerClient.GetBlobClient(blobName);

			try
			{
				var parsedDateTimeOffset = DateTimeOffset.ParseExact(blobName.Replace(".csv", string.Empty).Replace(BACKUP_PREFIX + maintenanceJob.DestinationStorageSetting.TargetName, string.Empty),
					BACKUP_DATE_FORMAT,
					null,
					DateTimeStyles.AssumeUniversal);

				var blobProperties = await blobClient.GetPropertiesAsync();
				var CreatedDate = parsedDateTimeOffset.UtcDateTime;
				// Use the blob property if it can be retrieved, otherwise parse time
				if (blobProperties.Value != null)
                {
					CreatedDate = blobProperties.Value.CreatedOn.UtcDateTime;
				}

				if (CreatedDate != default(DateTime) && CreatedDate < cutOffDate)
				{
					return true;
				}
			}
			catch
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"The blob item {blobName} did not have a CreatedOn property and its name was unparseable" });
			}

			return false;
		}

		public async Task CleanupAsync(IAzureMaintenanceJob maintenanceJob, string backupName)
		{
            var blobContainerClient = await GetContainerClient(maintenanceJob);
			if (blobContainerClient == null)
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"The blob container {GetContainerName(maintenanceJob)} did not exist when trying to delete blob {backupName}." });
				return;
			}

			// This only gives us a "true" or a "false". I assume a "false" means the blob didn't exist.
			// I think the only way this could try to delete a blob that doesn't exist is if there's some kind of weird race condition
			// that leads to a blob getting deleted twice, and that's fine, too.
			var response = await blobContainerClient.DeleteBlobIfExistsAsync(backupName);
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Got {response.Value} when deleting blob {backupName}." });
		}

		private async Task<BlobContainerClient> GetContainerClient(IAzureMaintenanceJob maintenanceJob)
        {
			BlobServiceClient blobServiceClient = new BlobServiceClient(maintenanceJob.DestinationStorageSetting.StorageConnectionString);
			var containerName = GetContainerName(maintenanceJob);
			var blobClient = blobServiceClient.GetBlobContainerClient(containerName);
			if (!await blobClient.ExistsAsync())
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"The blob container {containerName} does not exist." });
				return null;
			}

			return blobClient;
		}
	}
}
