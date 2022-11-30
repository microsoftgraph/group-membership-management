// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Data.Tables;
using Entities;
using Entities.AzureMaintenance;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using Repositories.Contracts;
using Repositories.Contracts.AzureMaintenance;
using Repositories.Contracts.InjectConfig;
using Services.Entities.Contracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TableEntity = Microsoft.Azure.Cosmos.Table.TableEntity;

namespace Repositories.AzureTableBackupRepository
{
    public class AzureTableBackupRepository : IAzureTableBackupRepository
    {
        private const string BACKUP_PREFIX = "zzBackup";
        private const string BACKUP_TABLE_NAME_SUFFIX = "BackupTracker";
        private const string BACKUP_DATE_FORMAT = "yyyyMMddHHmmss";
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IStorageAccountSecret _storageAccountSecret = null;

        public AzureTableBackupRepository(ILoggingRepository loggingRepository, IStorageAccountSecret storageAccountSecret)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _storageAccountSecret = storageAccountSecret ?? throw new ArgumentNullException(nameof(storageAccountSecret));
        }

        public async Task<List<BackupEntity>> GetBackupsAsync(IAzureMaintenanceJob maintenanceJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Getting backup tables for table {maintenanceJob.SourceStorageSetting.TargetName}" });

            var storageAccount = CloudStorageAccount.Parse(maintenanceJob.DestinationStorageSetting.StorageConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();

            List<CloudTable> tables;

            if (maintenanceJob.SourceStorageSetting.TargetName == "*")
            {
                tables = tableClient.ListTables().ToList();
            }
            else
            {
                tables = tableClient.ListTables(prefix: BACKUP_PREFIX + maintenanceJob.DestinationStorageSetting.TargetName).ToList();
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found {tables.Count} backup tables for table {maintenanceJob.SourceStorageSetting.TargetName}" });

            return tables.Select(table => new BackupEntity(table.Name, "table")).ToList();
        }

        public async Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureMaintenanceJob backupSettings)
        {
            var table = await GetCloudTableAsync(backupSettings.SourceStorageSetting.StorageConnectionString, backupSettings.SourceStorageSetting.TargetName);
            var entities = new List<DynamicTableEntity>();
            var query = table.CreateQuery<DynamicTableEntity>().AsTableQuery();

            if (!(await table.ExistsAsync()))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Source table {backupSettings.SourceStorageSetting.TargetName} was not found!" });
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

        public async Task DeleteBackupTrackersAsync(IAzureMaintenanceJob backupSettings, List<(string PartitionKey, string RowKey)> entities)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting old backup trackers from {backupSettings.SourceStorageSetting.TargetName}" });

            var batchSize = 100;
            var currentSize = 0;
            var table = await GetCloudTableAsync(backupSettings.DestinationStorageSetting.StorageConnectionString, backupSettings.SourceStorageSetting.TargetName + BACKUP_TABLE_NAME_SUFFIX);
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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted {deletedEntitiesCount} old backup trackers from {backupSettings.SourceStorageSetting.TargetName}" });
        }

        public async Task<BackupResult> BackupEntitiesAsync(IAzureMaintenanceJob maintenanceJob, List<DynamicTableEntity> entities)
        {
            var tableName = $"{BACKUP_PREFIX}{maintenanceJob.DestinationStorageSetting.TargetName}{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}";
            var table = await GetCloudTableAsync(maintenanceJob.DestinationStorageSetting.StorageConnectionString, tableName);

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

        public async Task<bool> VerifyCleanupAsync(IAzureMaintenanceJob maintenanceJob, string tableName)
        {
            var cutOffDate = DateTime.UtcNow.AddDays(-maintenanceJob.DeleteAfterDays);

            if (maintenanceJob.SourceStorageSetting.TargetName == "*")
            {
                var table = await GetCloudTableAsync(maintenanceJob.DestinationStorageSetting.StorageConnectionString, tableName);

                // Do not delete empty tables
                var takeOneQuery = table.CreateQuery<TableEntity>().AsQueryable().Take(1);
                var takeOneResults = table.ExecuteQuery(takeOneQuery.AsTableQuery()).ToList();
                if(takeOneResults.Count == 0)
                {
                    return false;
                }

                var cutoffQuery = table.CreateQuery<TableEntity>().AsQueryable().Where(e => e.Timestamp >= cutOffDate).Take(1);
                var cutoffResults = table.ExecuteQuery(cutoffQuery.AsTableQuery()).ToList();

                if (cutoffResults.Count == 0)
                {
                    return true;
                }
            }
            else
            {
                var CreatedDate = DateTime.SpecifyKind(
                    DateTime.ParseExact(tableName.Replace(BACKUP_PREFIX + maintenanceJob.DestinationStorageSetting.TargetName, string.Empty),
                        BACKUP_DATE_FORMAT,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal),
                    DateTimeKind.Utc);
                if (CreatedDate < cutOffDate)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task CleanupAsync(IAzureMaintenanceJob maintenanceJob, string tableName)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting backup table: {tableName}" });

            var table = await GetCloudTableAsync(maintenanceJob.DestinationStorageSetting.StorageConnectionString, tableName);

            if (!await table.ExistsAsync())
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Table not found : {tableName}" });
                return;
            }

            await table.DeleteIfExistsAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted backup table: {tableName}" });
        }

        private bool IsSuccessStatusCode(int statusCode) => statusCode >= 200 && statusCode <= 299;

        public async Task AddBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings, BackupResult backupResult)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Creating backup tracker for {backupSettings.SourceStorageSetting.TargetName}" });

            var table = await GetCloudTableAsync(backupSettings.DestinationStorageSetting.StorageConnectionString, backupSettings.SourceStorageSetting.TargetName + BACKUP_TABLE_NAME_SUFFIX);

            if (!await table.ExistsAsync())
            {
                await table.CreateIfNotExistsAsync();
            }

            backupResult.PartitionKey = backupSettings.SourceStorageSetting.TargetName;
            backupResult.RowKey = backupResult.BackupTableName;

            await table.ExecuteAsync(TableOperation.Insert(backupResult));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Created backup tracker ({backupResult.RowKey}) for {backupSettings.SourceStorageSetting.TargetName}" });
        }

        public async Task<BackupResult> GetLastestBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Getting latest backup tracker for {backupSettings.SourceStorageSetting.TargetName}" });

            var table = await GetCloudTableAsync(backupSettings.DestinationStorageSetting.StorageConnectionString, backupSettings.SourceStorageSetting.TargetName + BACKUP_TABLE_NAME_SUFFIX);

            if (!await table.ExistsAsync())
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"No backup tracker found for {backupSettings.SourceStorageSetting.TargetName}" });
                return null;
            }

            var results = new List<BackupResult>();
            var query = table.CreateQuery<BackupResult>().Where(x => x.PartitionKey == backupSettings.SourceStorageSetting.TargetName).AsTableQuery();

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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found latest backup tracker ([{backupResult.Timestamp.UtcDateTime}] - {backupResult.RowKey}) for {backupSettings.SourceStorageSetting.TargetName}" });

            return backupResult;
        }

        public async Task<int> BackupInactiveJobsAsync(List<SyncJob> syncJobs)
        {
            var backupCount = 0;

            var tableName = $"InactiveJobs{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}";
            var tableClient = new TableClient(_storageAccountSecret.ConnectionString, tableName);

            await tableClient.CreateIfNotExistsAsync();

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Backing up inactive jobs to table: {tableName} started",
                    DynamicProperties = { { "status", "Started" } }
                });

            var groups = syncJobs.GroupBy(x => x.PartitionKey).ToList();

            foreach (var group in groups)
            {
                foreach (var job in group)
                {
                    try
                    {
                        tableClient.AddEntity(job);
                        backupCount++;
                    }
                    catch (Exception ex)
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to add {job.RowKey} to table: {tableName}.\n{ex}" });
                    }                   
                    
                    await _loggingRepository.LogMessageAsync( new LogMessage { Message = $"Backing up inactive job with RowKey: {job.RowKey} to table: {tableName}" });
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

            return backupCount;
        }

        public async Task<List<BackupTable>> GetInactiveBackupsAsync()
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Getting backup tables containing inactive jobs" });

            var storageAccount = CloudStorageAccount.Parse(_storageAccountSecret.ConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            var tables = tableClient.ListTables(prefix: "InactiveJobs").ToList();
            var backupCloudTables = new List<BackupTable>();

            foreach (var table in tables)
            {
                var backupTable = new BackupTable
                {
                    TableName = table.Name,
                    CreatedDate = DateTime.SpecifyKind(
                                            DateTime.ParseExact(table.Name.Replace("InactiveJobs", string.Empty),
                                                BACKUP_DATE_FORMAT,
                                                CultureInfo.InvariantCulture,
                                                DateTimeStyles.AssumeUniversal),
                                        DateTimeKind.Utc)
                };

                backupCloudTables.Add(backupTable);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found {tables.Count} backup tables containing inactive jobs" });

            return backupCloudTables;

        }

        public async Task DeleteBackupTableAsync(string tableName)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting backup table: {tableName}" });

            var table = await GetCloudTableAsync(_storageAccountSecret.ConnectionString, tableName);

            if (!await table.ExistsAsync())
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Table not found : {tableName}" });
                return;
            }

            await table.DeleteIfExistsAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted backup table: {tableName}" });
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
