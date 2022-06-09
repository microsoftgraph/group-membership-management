// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Azure.Data.Tables;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.SyncJobsRepository
{
    public class SyncJobRepository : ISyncJobRepository
    {
        private readonly TableClient _tableClient = null;
        private readonly string _syncJobsTableName = null;
        private readonly ILoggingRepository _log;

        public SyncJobRepository(string connectionString, string syncJobTableName, ILoggingRepository logger)
        {
            _syncJobsTableName = syncJobTableName;
            _log = logger;
            _tableClient = new TableClient(connectionString, syncJobTableName);
        }

        public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
        {
            var result = await _tableClient.GetEntityAsync<SyncJob>(partitionKey, rowKey);

            if (result.GetRawResponse().Status != 404)
                return result.Value;

            return null;
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool applyFilters = true)
        {
            var queryResult = status == SyncStatus.All ?
            _tableClient.QueryAsync<SyncJob>() :
            _tableClient.QueryAsync<SyncJob>(x => x.Status == status.ToString());

            await foreach (var segmentResult in queryResult.AsPages())
            {
                if (segmentResult.Values.Count == 0)
                    await _log.LogMessageAsync(new LogMessage { Message = $"Warning: Number of enabled jobs in your sync jobs table is: {segmentResult.Values.Count}. Please confirm this is the case.", RunId = Guid.Empty });

                var results = applyFilters ? ApplyFilters(segmentResult.Values) : segmentResult.Values;

                foreach (var job in results)
                {
                    yield return job;
                }
            }
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds)
        {

            foreach (var (partitionKey, rowKey) in jobIds)
            {
                var tableResult = await _tableClient.GetEntityAsync<SyncJob>(partitionKey, rowKey);
                yield return tableResult.Value;
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
            var groupedJobs = jobs.GroupBy(x => x.PartitionKey);

            await _log.LogMessageAsync(new LogMessage { Message = $"Number of grouped jobs: {groupedJobs.Count()}", RunId = Guid.Empty });
            await _log.LogMessageAsync(new LogMessage { Message = $"Batching jobs by partition key started", RunId = Guid.Empty });

            foreach (var group in groupedJobs)
            {
                var batchOperation = new List<TableTransactionAction>();

                foreach (var job in group.AsEnumerable())
                {
                    if (status != null)
                    {
                        job.Status = status.ToString();
                        await _log.LogMessageAsync(new LogMessage { Message = $"Setting job status to {status} for job Rowkey:{job.RowKey}", RunId = job.RunId });
                    }

                    job.ETag = Azure.ETag.All;

					await _log.LogMessageAsync(new LogMessage { Message = string.Join('\n', job.GetType().GetProperties().Select(jobProperty => $"{jobProperty.Name} : {jobProperty.GetValue(job, null)}")), RunId = job.RunId });

					batchOperation.Add(new TableTransactionAction(TableTransactionActionType.UpdateReplace, job));

                    if (batchOperation.Count == batchSize)
                    {
                        await _tableClient.SubmitTransactionAsync(batchOperation);
                        batchOperation.Clear();
                    }
                }

                if (batchOperation.Any())
                {
                    await _tableClient.SubmitTransactionAsync(batchOperation);
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
    }
}
