// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Data.Tables;
using Models;
using Models.AzureMaintenance;
using Repositories.Contracts;
using Repositories.Contracts.AzureMaintenance;
using Repositories.Contracts.InjectConfig;
using Services.Entities.Contracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Repositories.SyncJobsRepository.Entities;
using Azure.Data.Tables.Models;

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

            var tableServiceClient = await GetTableServiceClientAsync(maintenanceJob.DestinationStorageSetting.StorageConnectionString);
            List<TableItem> tables = null;

            if (maintenanceJob.SourceStorageSetting.TargetName == "*")
            {
                tables = tableServiceClient.Query().ToList();
            }
            else
            {
                tables = ListTablesByPrefix(tableServiceClient, BACKUP_PREFIX + maintenanceJob.DestinationStorageSetting.TargetName);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found {tables.Count} backup tables for table {maintenanceJob.SourceStorageSetting.TargetName}" });

            return tables.Select(table => new BackupEntity(table.Name, "table")).ToList();
        }

        public async Task<List<TableEntity>> GetEntitiesAsync(IAzureMaintenanceJob backupSettings)
        {
            if (!TableExists(backupSettings.SourceStorageSetting.TargetName))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Source table {backupSettings.SourceStorageSetting.TargetName} was not found!" });
                return null;
            }

            var tableClient = new TableClient(backupSettings.SourceStorageSetting.StorageConnectionString, backupSettings.SourceStorageSetting.TargetName);
            return tableClient.Query<TableEntity>().ToList();
        }

        public async Task DeleteBackupTrackersAsync(IAzureMaintenanceJob backupSettings, List<(string PartitionKey, string RowKey)> entities)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting old backup trackers from {backupSettings.SourceStorageSetting.TargetName}" });

            var batchSize = 100;
            var currentSize = 0;
            var tableName = backupSettings.SourceStorageSetting.TargetName + BACKUP_TABLE_NAME_SUFFIX;
            var tableClient = new TableClient(backupSettings.DestinationStorageSetting.StorageConnectionString, tableName);

            var groupedEntities = entities.GroupBy(x => x.PartitionKey);
            var deletedEntitiesCount = 0;

            foreach (var group in groupedEntities)
            {
                var deleteBatchOperation = new List<TableTransactionAction>();

                foreach (var entity in group.AsEnumerable())
                {
                    var entityToDelete = tableClient.GetEntity<BackupResultEntity>(entity.PartitionKey, entity.RowKey);
                    if (entityToDelete.GetRawResponse().Status != 404)
                        deleteBatchOperation.Add(new TableTransactionAction(TableTransactionActionType.Delete, entityToDelete.Value));

                    if (++currentSize == batchSize)
                    {
                        var deleteResponse = await tableClient.SubmitTransactionAsync(deleteBatchOperation);
                        deletedEntitiesCount += deleteResponse.Value.Count(x => IsSuccessStatusCode(x.Status));

                        deleteBatchOperation.Clear();
                        currentSize = 0;
                    }
                }

                if (deleteBatchOperation.Any())
                {
                    var deleteResponse = await tableClient.SubmitTransactionAsync(deleteBatchOperation);
                    deletedEntitiesCount += deleteResponse.Value.Count(x => IsSuccessStatusCode(x.Status));
                }
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted {deletedEntitiesCount} old backup trackers from {backupSettings.SourceStorageSetting.TargetName}" });
        }

        public async Task<BackupResult> BackupEntitiesAsync(IAzureMaintenanceJob maintenanceJob, List<TableEntity> entities)
        {
            var tableName = $"{BACKUP_PREFIX}{maintenanceJob.DestinationStorageSetting.TargetName}{DateTime.UtcNow.ToString(BACKUP_DATE_FORMAT)}";
            var tableClient = new TableClient(maintenanceJob.DestinationStorageSetting.StorageConnectionString, tableName);

            if (!TableExists(tableName))
            {
                await tableClient.CreateIfNotExistsAsync();
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
                var batchOperation = new List<TableTransactionAction>();

                foreach (var job in group)
                {
                    batchOperation.Add(new TableTransactionAction(TableTransactionActionType.Add, job));

                    if (++currentSize == batchSize)
                    {
                        var result = await tableClient.SubmitTransactionAsync(batchOperation);
                        backupCount += result.Value.Count(x => IsSuccessStatusCode(x.Status));
                        batchOperation.Clear();
                        currentSize = 0;
                    }
                }

                if (batchOperation.Any())
                {
                    var result = await tableClient.SubmitTransactionAsync(batchOperation);
                    backupCount += result.Value.Count(x => IsSuccessStatusCode(x.Status));
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
                var tableServiceClient = await GetTableServiceClientAsync(maintenanceJob.DestinationStorageSetting.StorageConnectionString);
                var tableClient = tableServiceClient.GetTableClient(tableName);

                // Do not delete empty tables
                var takeOneQuery = tableClient.Query<TableEntity>().AsQueryable().Take(1);
                var takeOneResults = takeOneQuery.ToList();
                if (takeOneResults.Count == 0)
                {
                    return false;
                }

                var cutoffQuery = tableClient.Query<TableEntity>().AsQueryable().Where(e => e.Timestamp >= cutOffDate).Take(1);
                var cutoffResults = cutoffQuery.ToList();

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

            var tableClient = new TableClient(maintenanceJob.DestinationStorageSetting.StorageConnectionString, tableName);

            if (!TableExists(tableName))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Table not found : {tableName}" });
                return;
            }

            await tableClient.DeleteAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted backup table: {tableName}" });
        }

        private bool IsSuccessStatusCode(int statusCode) => statusCode >= 200 && statusCode <= 299;

        public async Task AddBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings, BackupResult backupResult)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Creating backup tracker for {backupSettings.SourceStorageSetting.TargetName}" });

            var tableName = backupSettings.SourceStorageSetting.TargetName + BACKUP_TABLE_NAME_SUFFIX;
            var tableClient = new TableClient(backupSettings.DestinationStorageSetting.StorageConnectionString, tableName);

            if (!TableExists(tableName))
            {
                await tableClient.CreateIfNotExistsAsync();
            }

            var entity = MapBackupTableToEntity(backupResult);
            entity.PartitionKey = backupSettings.SourceStorageSetting.TargetName;
            entity.RowKey = backupResult.BackupTableName;
            await tableClient.UpsertEntityAsync(entity);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Created backup tracker ({entity.RowKey}) for {backupSettings.SourceStorageSetting.TargetName}" });
        }

        public async Task<BackupResult> GetLatestBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Getting latest backup tracker for {backupSettings.SourceStorageSetting.TargetName}" });

            var tableName = backupSettings.SourceStorageSetting.TargetName + BACKUP_TABLE_NAME_SUFFIX;
            var tableClient = new TableClient(backupSettings.DestinationStorageSetting.StorageConnectionString, tableName);

            if (!TableExists(tableName))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"No backup tracker found for {backupSettings.SourceStorageSetting.TargetName}" });
                return null;
            }

            var backupResultEntities = tableClient.Query<BackupResultEntity>(x => x.PartitionKey == backupSettings.SourceStorageSetting.TargetName).ToList();
            var backupResult = backupResultEntities.OrderByDescending(x => x.Timestamp).FirstOrDefault();

            // if this is null (because the record predates table and blob backup) assume table
            backupResult.BackedUpTo = backupResult.BackedUpTo ?? "table";
            var trackerTimestamp = backupResult.Timestamp.HasValue ? backupResult.Timestamp.Value.UtcDateTime : (DateTime?)null;
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found latest backup tracker ([{trackerTimestamp}] - {backupResult.RowKey}) for {backupSettings.SourceStorageSetting.TargetName}" });

            return MapBackupTableEntityToDTO(backupResult);
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
                        tableClient.AddEntity(MapSyncJobToEntity(job));
                        backupCount++;
                    }
                    catch (Exception ex)
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to add {job.RowKey} to table: {tableName}.\n{ex}" });
                    }

                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Backing up inactive job with RowKey: {job.RowKey} to table: {tableName}" });
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

            var tableServiceClient = await GetTableServiceClientAsync(_storageAccountSecret.ConnectionString);
            var tables = ListTablesByPrefix(tableServiceClient, "InactiveJobs");
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

            var tableServiceClient = new TableServiceClient(_storageAccountSecret.ConnectionString);

            if (!TableExists(tableName))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Table not found : {tableName}" });
                return;
            }

            await tableServiceClient.DeleteTableAsync(tableName);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted backup table: {tableName}" });
        }

        private bool TableExists(string tableName)
        {
            var tableClient = new TableServiceClient(_storageAccountSecret.ConnectionString);
            return tableClient.Query(x => x.Name == tableName).Any();
        }

        private List<TableItem> ListTablesByPrefix(TableServiceClient tableServiceClient, string prefix)
        {
            var prefixQuery = TableClient.CreateQueryFilter<TableItem>(
                  x => x.Name.CompareTo(prefix) >= 0
                    && x.Name.CompareTo(prefix + char.MaxValue) <= 0);

            return tableServiceClient.Query(prefixQuery).ToList();
        }

        private async Task<TableServiceClient> GetTableServiceClientAsync(string connectionString)
        {
            try
            {
                return new TableServiceClient(connectionString);
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

        private SyncJobEntity MapSyncJobToEntity(SyncJob job)
        {
            return new SyncJobEntity(job.PartitionKey, job.RowKey)
            {
                IgnoreThresholdOnce = job.IgnoreThresholdOnce,
                IsDryRunEnabled = job.IsDryRunEnabled,
                DryRunTimeStamp = job.DryRunTimeStamp,
                LastRunTime = job.LastRunTime,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                LastSuccessfulStartTime = job.LastSuccessfulStartTime,
                StartDate = job.StartDate,
                Timestamp = job.Timestamp,
                TargetOfficeGroupId = job.TargetOfficeGroupId,
                RunId = job.RunId,
                Period = job.Period,
                ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals,
                ThresholdViolations = job.ThresholdViolations,
                ETag = string.IsNullOrWhiteSpace(job.ETag) ? ETag.All : new ETag(job.ETag),
                Query = job.Query,
                Requestor = job.Requestor,
                Status = job.Status,
            };
        }

        private BackupResult MapBackupTableEntityToDTO(BackupResultEntity backupTableEntity)
        {
            return new BackupResult
            {
                BackupTableName = backupTableEntity.BackupTableName,
                BackedUpTo = backupTableEntity.BackedUpTo,
                RowCount = backupTableEntity.RowCount
            };
        }

        private List<BackupResult> MapBackupTableEntitiesToDTOs(IEnumerable<BackupResultEntity> backupTableEntities)
        {
            return backupTableEntities.Select(x => MapBackupTableEntityToDTO(x)).ToList();
        }

        private BackupResultEntity MapBackupTableToEntity(BackupResult backupTable)
        {
            return new BackupResultEntity
            {
                BackupTableName = backupTable.BackupTableName,
                BackedUpTo = backupTable.BackedUpTo,
                RowCount = backupTable.RowCount
            };
        }
    }
}
