// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Entities;
using Models;
using Repositories.Contracts;
using Repositories.SyncJobsRepository.Entities;
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
            var result = await _tableClient.GetEntityAsync<SyncJobEntity>(partitionKey, rowKey);

            if (result.GetRawResponse().Status != 404)
                return MapSyncJobEntityToDTO(result.Value);

            return null;
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(bool includeFutureJobs = false, params SyncStatus[] statusFilters)
        {
            string query = null;

            if (statusFilters.Contains(SyncStatus.All))
            {
                if (!includeFutureJobs)
                    query = $"StartDate le datetime\'{DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff")}Z\'";
            }
            else
            {
                query = string.Join(" or ", statusFilters.Select(status => $"Status eq \'{status}\'"));
                if (!includeFutureJobs)
                    query = $"({query}) and StartDate le datetime\'{DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff")}Z\'";
            }

            var jobs = _tableClient.QueryAsync<SyncJobEntity>(query);
            await foreach (var job in jobs)
            {
                yield return MapSyncJobEntityToDTO(job);
            }
        }

        private async Task<Models.Page<SyncJob>> GetPageAsync(
            string query,
            string continuationToken = null,
            int? pageSize = null)
        {
            AsyncPageable<SyncJobEntity> tableQuery = _tableClient.QueryAsync<SyncJobEntity>(query);
            IAsyncEnumerator<Azure.Page<SyncJobEntity>> pageEnumerator = tableQuery
                                                                    .AsPages(continuationToken: continuationToken, pageSizeHint: pageSize)
                                                                    .GetAsyncEnumerator();
            await pageEnumerator.MoveNextAsync();
            Azure.Page<SyncJobEntity> page = pageEnumerator.Current;
            await pageEnumerator.DisposeAsync();

            return new Models.Page<SyncJob>
            {
                Query = query,
                Values = MapSyncJobEntitiesToDTOs(page.Values),
                ContinuationToken = page.ContinuationToken,
            };
        }

        public async Task<Models.Page<SyncJob>> GetPageableQueryResultAsync(
            bool includeFutureJobs,
            int? pageSize = null,
            params SyncStatus[] statusFilters)
        {
            string query = null;
            var result = new Models.Page<SyncJob>();

            if (statusFilters.Contains(SyncStatus.All))
            {
                if (!includeFutureJobs)
                    query = $"StartDate le datetime\'{DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff")}Z\'";

                return await GetPageAsync(query, pageSize: pageSize);
            }

            query = string.Join(" or ", statusFilters.Select(status => $"Status eq \'{status}\'"));

            if (!includeFutureJobs)
                query = $"({query}) and StartDate le datetime\'{DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff")}Z\'";

            return await GetPageAsync(query);
        }

        public async Task<Models.Page<SyncJob>> GetSyncJobsSegmentAsync(
           string query,
           string continuationToken,
           int batchSize)
        {
            return await GetPageAsync(query, continuationToken, batchSize);
        }

        public async IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync()
        {
            var queryResult = _tableClient.QueryAsync<SyncJobEntity>(x =>
                x.Status == SyncStatus.CustomerPaused.ToString() ||
                x.Status == SyncStatus.CustomMembershipDataNotFound.ToString() ||
                x.Status == SyncStatus.DestinationGroupNotFound.ToString() ||
                x.Status == SyncStatus.NotOwnerOfDestinationGroup.ToString() ||
                x.Status == SyncStatus.SecurityGroupNotFound.ToString() ||
                x.Status == SyncStatus.ThresholdExceeded.ToString());

            await foreach (var segmentResult in queryResult.AsPages())
            {
                if (segmentResult.Values.Count == 0)
                    await _log.LogMessageAsync(new LogMessage { Message = $"Number of inactive jobs in your sync jobs table is: {segmentResult.Values.Count}.", RunId = Guid.Empty });

                var results = segmentResult.Values.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromDays(30)));

                foreach (var job in results)
                {
                    yield return MapSyncJobEntityToDTO(job);
                }
            }
        }

        public async Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs)
        {
            var batchSize = 100;
            var groupedJobs = MapSyncJobsToEntities(jobs).GroupBy(x => x.PartitionKey);

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
            var groupedJobs = MapSyncJobsToEntities(jobs).GroupBy(x => x.PartitionKey);

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
            var entities = MapUpdateMergeSyncJobsToEntities(jobs);
            var batchOperation = new List<TableTransactionAction>();

            if (entities.Count() > 100)
            {
                entities = entities.Take(100).ToList();

                await _log.LogMessageAsync(new LogMessage
                {
                    Message = $"ERROR: This batch is more than 100, which is not supported by storage tables. Only the first 100 will be updated. Please fix the logic!",
                    RunId = Guid.Empty
                });
            }

            foreach (var job in entities)
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

        private IEnumerable<SyncJob> ExcludeFutureStartDatesFromResults(IEnumerable<SyncJob> jobs)
        {
            var jobsWithPastStartDate = jobs.Where(x => x.StartDate <= DateTime.UtcNow);
            return jobsWithPastStartDate;
        }

        private UpdateMergeSyncJobEntity MapUpdateMergeSyncJobToEntity(UpdateMergeSyncJob updateMergeSyncJob)
        {
            return new UpdateMergeSyncJobEntity
            {
                PartitionKey = updateMergeSyncJob.PartitionKey,
                RowKey = updateMergeSyncJob.RowKey,
                StartDate = updateMergeSyncJob.StartDate
            };
        }

        private List<UpdateMergeSyncJobEntity> MapUpdateMergeSyncJobsToEntities(IEnumerable<UpdateMergeSyncJob> jobs)
        {
            return jobs.Select(x => MapUpdateMergeSyncJobToEntity(x)).ToList();
        }

        private SyncJob MapSyncJobEntityToDTO(SyncJobEntity job)
        {
            return new SyncJob(job.PartitionKey, job.RowKey)
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
                Destination = job.Destination,
                AllowEmptyDestination = job.AllowEmptyDestination,
                RunId = job.RunId,
                Period = job.Period,
                ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals,
                ThresholdViolations = job.ThresholdViolations,
                ETag = job.ETag.ToString(),
                Query = job.Query,
                Requestor = job.Requestor,
                Status = job.Status
            };
        }

        private List<SyncJob> MapSyncJobEntitiesToDTOs(IEnumerable<SyncJobEntity> jobEntities)
        {
            return jobEntities.Select(x => MapSyncJobEntityToDTO(x)).ToList();
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
                Destination = job.Destination,
                AllowEmptyDestination = job.AllowEmptyDestination,
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

        private List<SyncJobEntity> MapSyncJobsToEntities(IEnumerable<SyncJob> jobEntities)
        {
            return jobEntities.Select(x => MapSyncJobToEntity(x)).ToList();
        }
    }
}
