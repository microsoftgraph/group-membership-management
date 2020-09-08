// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Services
{
    public class SyncJobTopicsService : ISyncJobTopicService
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository;

        public SyncJobTopicsService(
            ILoggingRepository loggingRepository,
            ISyncJobRepository syncJobRepository,
            IServiceBusTopicsRepository serviceBusTopicsRepository
            )
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
        }

        public async Task ProcessSyncJobsAsync()
        {
            var jobs = _syncJobRepository.GetSyncJobsAsync();

            var RunId = Guid.NewGuid();
            await foreach (var job in jobs)
            {
                job.RunId = RunId;
                var _ = _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    RunId = RunId,
                    Message = $"Job RunId: {job.RunId}, Partition Key: {job.PartitionKey}, Row Key: {job.RowKey}"
                });
                _ = _serviceBusTopicsRepository.AddMessageAsync(job);
            }
        }
    }
}

