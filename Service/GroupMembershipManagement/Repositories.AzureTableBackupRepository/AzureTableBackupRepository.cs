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
            var storageAccount = CloudStorageAccount.Parse(backupSettings.SourceConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            var table = tableClient.GetTableReference(backupSettings.SourceTableName);

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
            var storageAccount = CloudStorageAccount.Parse(backupSettings.DestinationConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            var table = tableClient.GetTableReference(tableName);
            table.CreateIfNotExists();

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Backing up data to table: {tableName} started.",
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
                    Message = $"Backing up data to table: {tableName} completed.",
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

            var storageAccount = CloudStorageAccount.Parse(backupSettings.DestinationConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            var table = tableClient.GetTableReference(tableName);

            if (table == null)
            {
                await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Table not found : {tableName}" });
                return;
            }

            await table.DeleteIfExistsAsync();

            await _loggingRepository.LogMessageAsync(new Entities.LogMessage { Message = $"Deleted backup table: {tableName}" });
        }

        private bool IsSuccessStatusCode(int statusCode) => statusCode >= 200 && statusCode <= 299;
    }
}
