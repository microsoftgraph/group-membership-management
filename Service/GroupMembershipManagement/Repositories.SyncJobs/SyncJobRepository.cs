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
        private readonly ILoggingRepository _log;

        public SyncJobRepository(string connectionString, string syncJobTableName, ILoggingRepository logger)
        {
            _syncJobsTableName = syncJobTableName;
            _log = logger;
            _cloudStorageAccount = CreateStorageAccountFromConnectionString(connectionString);
            _tableClient = _cloudStorageAccount.CreateCloudTableClient(new TableClientConfiguration());
            _tableClient.GetTableReference(syncJobTableName).CreateIfNotExists();
        }

        public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
        {
            var table = _tableClient.GetTableReference(_syncJobsTableName);
            var result = await table.ExecuteAsync(TableOperation.Retrieve<SyncJob>(partitionKey, rowKey));

            if (result.HttpStatusCode != 404)
                return result.Result as SyncJob;

            return null;
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool includeDisabled = false, bool applyFilters = true)
        {
            var syncJobs = new List<SyncJob>();
            var table = _tableClient.GetTableReference(_syncJobsTableName);
            var linqQuery = table.CreateQuery<SyncJob>().AsQueryable();

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

                if (segmentResult.Results.Count == 0)
                    await _log.LogMessageAsync(new LogMessage { Message = $"Warning: Number of enabled jobs in your sync jobs table is: {segmentResult.Results.Count}. Please confirm this is the case.", RunId = Guid.Empty });

                var results = applyFilters ? ApplyFilters(segmentResult.Results) : segmentResult.Results;

                foreach (var job in results)
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
            await UpdateSyncJobsAsync(jobs, status: status);
        }

        public async Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null)
        {
            var batchSize = 100;
            var currentSize = 0;
            var table = _tableClient.GetTableReference(_syncJobsTableName);
            var groupedJobs = jobs.GroupBy(x => x.PartitionKey);

            await _log.LogMessageAsync(new LogMessage { Message = $"Number of grouped jobs: {groupedJobs.Count()}", RunId = Guid.Empty });
            await _log.LogMessageAsync(new LogMessage { Message = $"Batching jobs by partition key started", RunId = Guid.Empty });

            foreach (var group in groupedJobs)
            {
                var batchOperation = new TableBatchOperation();

                foreach (var job in group.AsEnumerable())
                {
                    if (status != null)
                    {
                        job.Status = status.ToString();
                    }

                    job.ETag = "*";

					await _log.LogMessageAsync(new LogMessage { Message = string.Join('\n', job.GetType().GetProperties().Select(jobProperty => $"{jobProperty.Name} : {jobProperty.GetValue(job, null)}")), RunId = job.RunId });

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
            await _log.LogMessageAsync(new LogMessage { Message = $"Batching jobs by partition key completed", RunId = Guid.Empty });
        }

        private IEnumerable<SyncJob> ApplyFilters(IEnumerable<SyncJob> jobs)
        {
            var jobsWithPastStartDate = jobs.Where(x => x.StartDate <= DateTime.UtcNow);
            var allNonDryRunSyncJobs = jobsWithPastStartDate.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == false);
            var allDryRunSyncJobs = jobsWithPastStartDate.Where(x => ((DateTime.UtcNow - x.DryRunTimeStamp) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == true);
            return allNonDryRunSyncJobs.Concat(allDryRunSyncJobs);
        }

        private CloudStorageAccount CreateStorageAccountFromConnectionString(string storageConnectionString)
        {
            return CloudStorageAccount.Parse(storageConnectionString);
        }
    }
}
