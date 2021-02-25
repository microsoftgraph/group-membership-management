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
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.AzureTableBackupRepository
{
    public class AzureTableBackupRepository : IAzureTableBackupRepository
    {
        private const string BACKUP_PREFIX = "Backup";
        private const string BACKUP_TABLE_NAME = "ATBBackupTracker";
        private readonly ILoggingRepository _loggingRepository = null;

        public AzureTableBackupRepository(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<List<CloudTable>> GetBackupTablesAsync(IAzureTableBackup backupSettings)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Getting backup tables for table {backupSettings.SourceTableName}" });

            var storageAccount = CloudStorageAccount.Parse(backupSettings.DestinationConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            var tables = tableClient.ListTables()
                                    .Where(x => x.Name.StartsWith(BACKUP_PREFIX + backupSettings.SourceTableName, StringComparison.InvariantCultureIgnoreCase))
                                    .ToList();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found {tables.Count} backup tables for table {backupSettings.SourceTableName}" });

            return tables;
        }

        public async Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureTableBackup backupSettings)
        {
            var table = GetCloudTable(backupSettings.DestinationConnectionString, backupSettings.SourceTableName);
            var entities = new List<DynamicTableEntity>();
            var query = table.CreateQuery<DynamicTableEntity>().AsTableQuery();

            TableContinuationToken continuationToken = null;
            do
            {
                var segmentResult = await table.ExecuteQuerySegmentedAsync(query, continuationToken);
                continuationToken = segmentResult.ContinuationToken;
                entities.AddRange(segmentResult.Results);

            } while (continuationToken != null);

            return entities;
        }

        public async Task<BackupResult> BackupEntitiesAsync(IAzureTableBackup backupSettings, List<DynamicTableEntity> entities)
        {
            var tableName = $"{BACKUP_PREFIX}{backupSettings.SourceTableName}{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}";
            var table = GetCloudTable(backupSettings.DestinationConnectionString, tableName);

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

            return new BackupResult(tableName, backupCount);
        }

        public async Task DeleteBackupTableAsync(IAzureTableBackup backupSettings, string tableName)
        {
            await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Deleting backup table: {tableName}" });

            var table = GetCloudTable(backupSettings.DestinationConnectionString, tableName);

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

            var table = GetCloudTable(backupSettings.DestinationConnectionString, BACKUP_TABLE_NAME);

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

            var table = GetCloudTable(backupSettings.DestinationConnectionString, BACKUP_TABLE_NAME);

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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found latest backup tracker ([{backupResult.Timestamp.UtcDateTime}] - {backupResult.RowKey}) for {backupSettings.SourceTableName}" });

            return backupResult;
        }

        private CloudTable GetCloudTable(string connectionString, string tableName)
        {
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            return tableClient.GetTableReference(tableName);
        }
    }
}
