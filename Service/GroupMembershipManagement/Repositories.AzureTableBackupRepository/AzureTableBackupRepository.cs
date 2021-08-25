// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.AzureTableBackup;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.AzureTableBackupRepository
{
    public class AzureTableBackupRepository : IAzureTableBackupRepository
    {
        private const string BACKUP_PREFIX = "_Backup";
        private const string BACKUP_TABLE_NAME_SUFFIX = "BackupTracker";
        private const string BACKUP_DATE_FORMAT = "yyyyMMddHHmmss";
        private readonly ILoggingRepository _loggingRepository = null;

        public AzureTableBackupRepository(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<List<BackupEntity>> GetBackupsAsync(IAzureTableBackup backupSettings)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Getting backup tables for table {backupSettings.SourceTableName}" });

            var storageAccount = CloudStorageAccount.Parse(backupSettings.DestinationConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            var tables = tableClient.ListTables(prefix: BACKUP_PREFIX + backupSettings.SourceTableName).ToList();
            var backupCloudTables = new List<BackupEntity>();

            foreach (var table in tables)
            {
                var backupTable = new BackupEntity
                {
                    Name = table.Name,
                    StorageType = "table",
                    CreatedDate = DateTime.SpecifyKind(
                                            DateTime.ParseExact(table.Name.Replace(BACKUP_PREFIX + backupSettings.SourceTableName, string.Empty),
                                                BACKUP_DATE_FORMAT,
                                                CultureInfo.InvariantCulture,
                                                DateTimeStyles.AssumeUniversal), 
                                        DateTimeKind.Utc)
                };

                backupCloudTables.Add(backupTable);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found {tables.Count} backup tables for table {backupSettings.SourceTableName}" });

            return backupCloudTables;
        }

        public async Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureTableBackup backupSettings)
        {
            var table = await GetCloudTableAsync(backupSettings.SourceConnectionString, backupSettings.SourceTableName);
            var entities = new List<DynamicTableEntity>();
            var query = table.CreateQuery<DynamicTableEntity>().AsTableQuery();

            if (!(await table.ExistsAsync()))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Source table {backupSettings.SourceTableName} was not found!" });
                return null;
            }

            TableContinuationToken continuationToken = null;
            do
            {
                var segmentResult = await table.ExecuteQuerySegmentedAsync(query, continuationToken);
                continuationToken = segmentResult.ContinuationToken;
                entities.AddRange(segmentResult.Results);

            } while (continuationToken != null);

            return entities;
        }

        public async Task DeleteBackupTrackersAsync(IAzureTableBackup backupSettings, List<(string PartitionKey, string RowKey)> entities)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting old backup trackers from {backupSettings.SourceTableName}" });

            var batchSize = 100;
            var currentSize = 0;
            var table = await GetCloudTableAsync(backupSettings.DestinationConnectionString, backupSettings.SourceTableName + BACKUP_TABLE_NAME_SUFFIX);
            var groupedEntities = entities.GroupBy(x => x.PartitionKey);
            var deletedEntitiesCount = 0;

            foreach (var group in groupedEntities)
            {
                var deleteBatchOperation = new TableBatchOperation();

                foreach (var entity in group.AsEnumerable())
                {
                    var entityToDelete = table.Execute(TableOperation.Retrieve<BackupResult>(entity.PartitionKey, entity.RowKey));
                    if (entityToDelete.HttpStatusCode != 404)
                        deleteBatchOperation.Delete(entityToDelete.Result as BackupResult);

                    if (++currentSize == batchSize)
                    {
                        var deleteResponse = await table.ExecuteBatchAsync(deleteBatchOperation);
                        deletedEntitiesCount += deleteResponse.Count(x => IsSuccessStatusCode(x.HttpStatusCode));

                        deleteBatchOperation = new TableBatchOperation();
                        currentSize = 0;
                    }
                }

                if (deleteBatchOperation.Any())
                {
                    var deleteResponse = await table.ExecuteBatchAsync(deleteBatchOperation);
                    deletedEntitiesCount += deleteResponse.Count(x => IsSuccessStatusCode(x.HttpStatusCode));
                }
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted {deletedEntitiesCount} old backup trackers from {backupSettings.SourceTableName}" });
        }

        public async Task<BackupResult> BackupEntitiesAsync(IAzureTableBackup backupSettings, List<DynamicTableEntity> entities)
        {
            var tableName = $"{BACKUP_PREFIX}{backupSettings.SourceTableName}{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}";
            var table = await GetCloudTableAsync(backupSettings.DestinationConnectionString, tableName);

            if (!await table.ExistsAsync())
            {
                await table.CreateIfNotExistsAsync();
            }

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Backing up data to table: {tableName} started",
                    DynamicProperties = { { "status", "Started" } }
                });

            var backupCount = 0;
            var batchSize = 100;
            var currentSize = 0;
            var groups = entities.GroupBy(x => x.PartitionKey).ToList();

            foreach (var group in groups)
            {
                var batchOperation = new TableBatchOperation();

                foreach (var job in group)
                {

                    batchOperation.Insert(job);

                    if (++currentSize == batchSize)
                    {
                        var result = await table.ExecuteBatchAsync(batchOperation);
                        backupCount += result.Count(x => IsSuccessStatusCode(x.HttpStatusCode));
                        batchOperation = new TableBatchOperation();
                        currentSize = 0;
                    }
                }

                if (batchOperation.Any())
                {
                    var result = await table.ExecuteBatchAsync(batchOperation);
                    backupCount += result.Count(x => IsSuccessStatusCode(x.HttpStatusCode));
                }
            }

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Backing up data to table: {tableName} completed",
                    DynamicProperties = {
                        { "status", "Completed" },
                        { "rowCount", backupCount.ToString() }
                }
                });

            return new BackupResult(tableName, "table", backupCount);
        }

        public async Task DeleteBackupAsync(IAzureTableBackup backupSettings, string tableName)
        {
            await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Deleting backup table: {tableName}" });

            var table = await GetCloudTableAsync(backupSettings.DestinationConnectionString, tableName);

            if (!await table.ExistsAsync())
            {
                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Table not found : {tableName}" });
                return;
            }

            await table.DeleteIfExistsAsync();

            await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Deleted backup table: {tableName}" });
        }

        private bool IsSuccessStatusCode(int statusCode) => statusCode >= 200 && statusCode <= 299;

        public async Task AddBackupResultTrackerAsync(IAzureTableBackup backupSettings, BackupResult backupResult)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Creating backup tracker for {backupSettings.SourceTableName}" });

            var table = await GetCloudTableAsync(backupSettings.DestinationConnectionString, backupSettings.SourceTableName + BACKUP_TABLE_NAME_SUFFIX);

            if (!await table.ExistsAsync())
            {
                await table.CreateIfNotExistsAsync();
            }

            backupResult.PartitionKey = backupSettings.SourceTableName;
            backupResult.RowKey = backupResult.BackupTableName;

            await table.ExecuteAsync(TableOperation.Insert(backupResult));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Created backup tracker ({backupResult.RowKey}) for {backupSettings.SourceTableName}" });
        }

        public async Task<BackupResult> GetLastestBackupResultTrackerAsync(IAzureTableBackup backupSettings)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Getting latest backup tracker for {backupSettings.SourceTableName}" });

            var table = await GetCloudTableAsync(backupSettings.DestinationConnectionString, backupSettings.SourceTableName + BACKUP_TABLE_NAME_SUFFIX);

            if (!await table.ExistsAsync())
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"No backup tracker found for {backupSettings.SourceTableName}" });
                return null;
            }

            var results = new List<BackupResult>();
            var query = table.CreateQuery<BackupResult>().Where(x => x.PartitionKey == backupSettings.SourceTableName).AsTableQuery();

            TableContinuationToken continuationToken = null;
            do
            {
                var segmentResult = await table.ExecuteQuerySegmentedAsync(query, continuationToken);
                continuationToken = segmentResult.ContinuationToken;
                results.AddRange(segmentResult.Results);

            } while (continuationToken != null);

            var backupResult = results.OrderByDescending(x => x.Timestamp).FirstOrDefault();

            // if this is null (because the record predates table and blob backup) assume table
            backupResult.BackedUpTo = backupResult.BackedUpTo ?? "table";

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found latest backup tracker ([{backupResult.Timestamp.UtcDateTime}] - {backupResult.RowKey}) for {backupSettings.SourceTableName}" });

            return backupResult;
        }

        private async Task<CloudTableClient> GetCloudTableClientAsync(string connectionString)
        {
            try
            {
                return CloudStorageAccount.Parse(connectionString).CreateCloudTableClient();
            }
            catch (FormatException)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Azure Table Storage connection string format is not valid" });
                throw;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to create cloud table client.\n{ex}" });
                throw;
            }

        }
        
        private async Task<CloudTable> GetCloudTableAsync(string connectionString, string tableName)
        {
            var cloudTableClient = await GetCloudTableClientAsync(connectionString);
            return cloudTableClient.GetTableReference(tableName);
        }
    }
}
