// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class SyncJobSubscriptionsService : ISyncJobSubscriptionsService
    {
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IServiceBusSubscriptionsRepository _serviceBusSubscriptionsRepository;

        public SyncJobSubscriptionsService(
            ISyncJobRepository syncJobRepository,
            IServiceBusSubscriptionsRepository serviceBusSubscriptionsRepository
            )
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository)); ;
            _serviceBusSubscriptionsRepository = serviceBusSubscriptionsRepository ?? throw new ArgumentNullException(nameof(serviceBusSubscriptionsRepository)); ;
        }

        public async Task ProcessSyncJobsAsync(string topicName, string subscriptionName)
        {
            var batchSize = 100;
            var jobIds = new List<(string, string)>();
            var messages = _serviceBusSubscriptionsRepository.GetMessagesAsync(topicName, subscriptionName);

            await foreach (var message in messages)
            {
                if (jobIds.Count == batchSize)
                {
                    await UpdateJobsStatus(jobIds);
                    jobIds.Clear();
                }

                jobIds.Add(
                    (message.UserProperties["PartitionKey"].ToString(),
                    message.UserProperties["RowKey"].ToString())
                    );
            }

            if (jobIds.Any())
            {
                await UpdateJobsStatus(jobIds);
            }
        }

        private async Task UpdateJobsStatus(IEnumerable<(string, string)> jobIds)
        {
            var jobs = _syncJobRepository.GetSyncJobsAsync(jobIds);
            var idleJobs = new List<SyncJob>();

            await foreach (var job in jobs)
            {
                if (job.Status == SyncStatus.Idle.ToString())
                {
                    idleJobs.Add(job);
                }
            }

            if (idleJobs.Any())
            {
                await _syncJobRepository.UpdateSyncJobStatusAsync(idleJobs, SyncStatus.InProgress);
            }
        }
    }
}

