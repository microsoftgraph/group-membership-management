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
			var containerName = $"{BACKUP_PREFIX}{backupSettings.SourceTableName}".ToLowerInvariant();
			var blobName = $"{BACKUP_PREFIX}{backupSettings.SourceTableName}{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}.csv";

			var blobClient = blobServiceClient.GetBlobContainerClient(containerName);
			await blobClient.CreateIfNotExistsAsync();

			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Backing {entities.Count} entries up to the blob named {blobName}." });
			var result = await blobClient.UploadBlobAsync(blobName, new BinaryData(SerializeEntities(entities)));
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Successfully backed up {entities.Count} entries to the blob named {blobName} successful." });
			// make sure it worked?

			return new BackupResult(blobName, entities.Count);
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
			var response = await blobServiceClient.DeleteBlobContainerAsync(backupName);
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Got status code {response.Status} when deleting blob {backupName}." });
		}
	}
}
