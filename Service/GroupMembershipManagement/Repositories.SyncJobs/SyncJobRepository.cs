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

        public async IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync()
        {
            var queryResult = _tableClient.QueryAsync<SyncJobEntity>(x =>
                x.Status == SyncStatus.CustomerPaused.ToString() ||
                x.Status == SyncStatus.MembershipDataNotFound.ToString() ||
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
