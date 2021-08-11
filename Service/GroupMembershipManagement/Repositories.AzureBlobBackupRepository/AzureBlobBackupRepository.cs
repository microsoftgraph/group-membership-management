using Azure.Storage.Blobs;
using CsvHelper;
using Entities;
using Entities.AzureTableBackup;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
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

		public async Task<BackupResult> BackupEntitiesAsync(IAzureTableBackup backupSettings, List<DynamicTableEntity> entities)
		{
			if (!entities.Any())
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Nothing to back up for {backupSettings.SourceTableName}. Skipping." });
			}
			BlobServiceClient blobServiceClient = new BlobServiceClient(backupSettings.DestinationConnectionString);
			string containerName = GetContainerName(backupSettings);
			var blobName = $"{BACKUP_PREFIX}{backupSettings.SourceTableName}{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}.csv";

			var blobClient = blobServiceClient.GetBlobContainerClient(containerName);
			await blobClient.CreateIfNotExistsAsync();

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Backing {entities.Count} entries up to the blob named {blobName}." });
			var result = await blobClient.UploadBlobAsync(blobName, new BinaryData(SerializeEntities(entities)));
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Successfully backed up {entities.Count} entries to the blob named {blobName} successful." });
			// make sure it worked?

			return new BackupResult(blobName, "blob", entities.Count);
		}

		private static string GetContainerName(IAzureTableBackup backupSettings)
		{
			return $"{BACKUP_PREFIX}{backupSettings.SourceTableName}".ToLowerInvariant();
		}

		private string SerializeEntities(List<DynamicTableEntity> entities)
		{
			var keys = entities[0].Properties.Keys;
			using var writer = new StringWriter();
			using (var csv = new CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture))
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

		public async Task DeleteBackupAsync(IAzureTableBackup backupSettings, string backupName)
		{
			BlobServiceClient blobServiceClient = new BlobServiceClient(backupSettings.DestinationConnectionString);
			var containerName = GetContainerName(backupSettings);
			var blobClient = blobServiceClient.GetBlobContainerClient(containerName);
			if(!await blobClient.ExistsAsync())
			{
				await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"The blob container {containerName} did not exist when trying to delete blob {backupName}." });
				return;
			}
			// This only gives us a "true" or a "false". I assume a "false" means the blob didn't exist.
			// I think the only way this could try to delete a blob that doesn't exist is if there's some kind of weird race condition
			// that leads to a blob getting deleted twice, and that's fine, too.
			var response = await blobClient.DeleteBlobIfExistsAsync(backupName);
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Got {response.Value} when deleting blob {backupName}." });
		}

		public async Task<List<BackupEntity>> GetBackupsAsync(IAzureTableBackup backupSettings)
		{
			BlobServiceClient blobServiceClient = new BlobServiceClient(backupSettings.DestinationConnectionString);
			var containerClient = blobServiceClient.GetBlobContainerClient(GetContainerName(backupSettings));
			var blobs = containerClient.GetBlobsAsync();
			var toReturn = new List<BackupEntity>();
			await foreach (var blob in blobs)
			{
				var parsedDateTimeOffset = DateTimeOffset.ParseExact(blob.Name.Replace(".csv", string.Empty).Replace(BACKUP_PREFIX + backupSettings.SourceTableName, string.Empty),
					BACKUP_DATE_FORMAT, 
					null,
					DateTimeStyles.AssumeUniversal);
				toReturn.Add(new BackupEntity { Name = blob.Name, StorageType = "blob", CreatedDate = blob.Properties.CreatedOn.GetValueOrDefault(parsedDateTimeOffset).UtcDateTime });
			}
			return toReturn;
		}
	}
}
