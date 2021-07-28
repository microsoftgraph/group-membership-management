using Azure.Storage.Blobs;
using CsvHelper;
using Entities.AzureTableBackup;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Repositories.AzureBlobBackupRepository
{
	public class AzureBlobBackupRepository
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
			BlobServiceClient blobServiceClient = new BlobServiceClient(backupSettings.DestinationConnectionString);
			var blobName = $"{BACKUP_PREFIX}{backupSettings.SourceTableName}{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}";
			BlobContainerClient blobClient = await blobServiceClient.CreateBlobContainerAsync(blobName);

			var result = await blobClient.UploadBlobAsync(blobName, new BinaryData(SerializeEntities(entities)));
			// make sure it worked?

			return new BackupResult(blobName, entities.Count);
		}

		private string SerializeEntities(List<DynamicTableEntity> entities)
		{
			using var writer = new StringWriter();
			using (var csv = new CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture))
			{
				csv.WriteRecords(entities);
				return writer.ToString();
			}
		}
	}
}
