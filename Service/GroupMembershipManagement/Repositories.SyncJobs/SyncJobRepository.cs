// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Entities;
using Models;
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
        private readonly ILoggingRepository _log;

        public SyncJobRepository(string connectionString, string syncJobTableName, ILoggingRepository logger)
        {
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

        public AsyncPageable<SyncJob> GetPageableQueryResult(
            SyncStatus status = SyncStatus.All,
            bool includeFutureJobs = false)
        {
            if (status == SyncStatus.All && includeFutureJobs)
            {
                return _tableClient.QueryAsync<SyncJob>();
            }
            else if (status != SyncStatus.All && includeFutureJobs)
            {
                return _tableClient.QueryAsync<SyncJob>(job => job.Status == status.ToString());
            }
            else if (status == SyncStatus.All && !includeFutureJobs)
            {
                return _tableClient.QueryAsync<SyncJob>(job => job.StartDate <= DateTime.UtcNow);
            }
            else
            {
                return _tableClient.QueryAsync<SyncJob>(job => job.Status == status.ToString() && job.StartDate <= DateTime.UtcNow);
            }
        }

        public async Task<TableSegmentBulkResult<DistributionSyncJob>> GetSyncJobsSegmentAsync(
            AsyncPageable<SyncJob> pageableQueryResult,
            string continuationToken,
            bool applyFilters = true)
        {
            var bulkSegment = new TableSegmentBulkResult<DistributionSyncJob>
            {
                Results = new List<DistributionSyncJob>()
            };

            var index = 0;
            var SEGMENT_BATCHSIZE = 100;

            var pageableResult = pageableQueryResult.AsPages(continuationToken);
            var pageEnumerator = pageableResult.GetAsyncEnumerator();

            await pageEnumerator.MoveNextAsync();

            try
            {
                do
                {
                    var segmentResult = pageEnumerator.Current;
                    var filteredResults = applyFilters ? ApplyDryRunFiltersToResults(segmentResult.Values) : segmentResult.Values;
                    var filteredDistributionSyncJobs = filteredResults.Select(job => new DistributionSyncJob(job));
                    bulkSegment.Results.AddRange(filteredDistributionSyncJobs);
                    continuationToken = segmentResult.ContinuationToken;

                    await pageEnumerator.MoveNextAsync();
                    index++;
                }
                while (continuationToken != null && index < SEGMENT_BATCHSIZE);
            }
            finally
            {
                await pageEnumerator.DisposeAsync();
            }

            bulkSegment.ContinuationToken = continuationToken;

            return bulkSegment;
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

                var results = applyFilters ? ApplyDryRunFiltersToResults(ExcludeFutureStartDatesFromResults(segmentResult.Values)) : segmentResult.Values;

                foreach (var job in results)
                {
                    yield return job;
                }
            }
        }

        public async IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync()
        {
            var queryResult = _tableClient.QueryAsync<SyncJob>(x => 
                    x.Status != SyncStatus.Idle.ToString() && 
                    x.Status != SyncStatus.InProgress.ToString() && 
                    x.Status != SyncStatus.Error.ToString());
                       
            await foreach (var segmentResult in queryResult.AsPages())
            {
                if (segmentResult.Values.Count == 0)
                    await _log.LogMessageAsync(new LogMessage { Message = $"Number of inactive jobs in your sync jobs table is: {segmentResult.Values.Count}.", RunId = Guid.Empty });

                var results = segmentResult.Values.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromDays(30)));

                foreach (var job in results)
                {
                    yield return job;
                }
            }
        }

        public async Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs)
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
                    job.ETag = ETag.All;

                    await _log.LogMessageAsync(new LogMessage { Message = string.Join('\n', job.GetType().GetProperties().Select(jobProperty => $"{jobProperty.Name} : {jobProperty.GetValue(job, null)}")), RunId = job.RunId });

                    batchOperation.Add(new TableTransactionAction(TableTransactionActionType.Delete, job));

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

                    job.ETag = ETag.All;

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

        public async Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> jobs)
        {
            var batchOperation = new List<TableTransactionAction>();

            if(jobs.Count() > 100)
            {
                jobs = jobs.Take(100).ToList();

                await _log.LogMessageAsync(new LogMessage
                {
                    Message = $"ERROR: This batch is more than 100, which is not supported by storage tables. Only the first 100 will be updated. Please fix the logic!",
                    RunId = Guid.Empty
                });
            }

            foreach (var job in jobs)
            {
                job.ETag = ETag.All;

                await _log.LogMessageAsync(new LogMessage { Message = string.Join('\n', job.GetType().GetProperties().Select(jobProperty => $"{jobProperty.Name} : {jobProperty.GetValue(job, null)}")) }, VerbosityLevel.DEBUG);

                batchOperation.Add(new TableTransactionAction(TableTransactionActionType.UpdateMerge, job));
            }

            if (batchOperation.Any())
            {
                await _tableClient.SubmitTransactionAsync(batchOperation);
            }
        }

        private IEnumerable<SyncJob> ApplyDryRunFiltersToResults(IEnumerable<SyncJob> jobs)
        {
            var allNonDryRunSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == false);
            var allDryRunSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.DryRunTimeStamp) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == true);
            return allNonDryRunSyncJobs.Concat(allDryRunSyncJobs);
        }

        private IEnumerable<SyncJob> ExcludeFutureStartDatesFromResults(IEnumerable<SyncJob> jobs)
        {
            var jobsWithPastStartDate = jobs.Where(x => x.StartDate <= DateTime.UtcNow);
            return jobsWithPastStartDate;
        }
    }
}
