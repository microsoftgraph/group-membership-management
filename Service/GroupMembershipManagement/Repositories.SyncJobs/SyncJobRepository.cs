// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.SyncJobsRepository
{
    public class SyncJobRepository : ISyncJobRepository
    {
        private readonly CloudStorageAccount _cloudStorageAccount = null;
        private readonly CloudTableClient _tableClient = null;
        private readonly string _syncJobsTableName = null;
        private readonly ILoggingRepository _log = null;

        public SyncJobRepository(string connectionString, string syncJobTableName, ILoggingRepository loggingRepository)
        {
            _syncJobsTableName = syncJobTableName;
            _cloudStorageAccount = CreateStorageAccountFromConnectionString(connectionString);
            _tableClient = _cloudStorageAccount.CreateCloudTableClient(new TableClientConfiguration());
            _tableClient.GetTableReference(syncJobTableName).CreateIfNotExistsAsync();
            _log = loggingRepository;
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool includeDisabled = false)
        {
            var syncJobs = new List<SyncJob>();
            var table = _tableClient.GetTableReference(_syncJobsTableName);

            var linqQuery = table.CreateQuery<SyncJob>().Where(x => x.StartDate <= DateTime.UtcNow);

            if (status != SyncStatus.All)
            {
                linqQuery = linqQuery.Where(x => x.Status == status.ToString());
            }

            if (!includeDisabled)
            {
                linqQuery = linqQuery.Where(x => x.Enabled);
            }

            TableContinuationToken continuationToken = null;
            do
            {
                var segmentResult = await table.ExecuteQuerySegmentedAsync(linqQuery.AsTableQuery(), continuationToken);
                continuationToken = segmentResult.ContinuationToken;

                foreach (var job in ApplyFilters(segmentResult.Results))
                {
                    yield return job;
                }

            } while (continuationToken != null);
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds)
        {
            var table = _tableClient.GetTableReference(_syncJobsTableName);

            foreach (var (partitionKey, rowKey) in jobIds)
            {
                var tableResult = await table.ExecuteAsync(TableOperation.Retrieve<SyncJob>(partitionKey, rowKey));
                yield return tableResult.Result as SyncJob;
            }
        }

        /// <summary>
        /// Update all the jobs with the specified status.
        /// </summary>
        /// <param name="jobs"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        public async Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status)
        {
            var batchSize = 100;
            var currentSize = 0;
            var table = _tableClient.GetTableReference(_syncJobsTableName);
            var groupedJobs = jobs.GroupBy(x => x.PartitionKey);

            await _log.LogMessageAsync(new LogMessage { Message = $"Batching jobs by partition key", RunId = Guid.Empty });

            foreach (var group in groupedJobs)
            {
                
                await _log.LogMessageAsync(new LogMessage { Message = $"Job: {group}", RunId = Guid.Empty });

                var batchOperation = new TableBatchOperation();

                foreach (var job in group.AsEnumerable())
                {
                    job.Status = status.ToString();
                    job.ETag = "*";

                    batchOperation.Add(TableOperation.Replace(job));

                    if (++currentSize == batchSize)
                    {
                        await table.ExecuteBatchAsync(batchOperation);
                        batchOperation = new TableBatchOperation();
                        currentSize = 0;
                    }
                }

                if (batchOperation.Any())
                {
                    await table.ExecuteBatchAsync(batchOperation);
                }
            }
            await _log.LogMessageAsync(new LogMessage { Message = $"Batching jobs by partition key complete", RunId = Guid.Empty });
        }

        private IEnumerable<SyncJob> ApplyFilters(IEnumerable<SyncJob> jobs)
        {
            return jobs.Where(x => (DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromHours(x.Period));
        }

        private CloudStorageAccount CreateStorageAccountFromConnectionString(string storageConnectionString)
        {
            try
            {
                return CloudStorageAccount.Parse(storageConnectionString);
            }
            catch (FormatException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
        }
    }
}
