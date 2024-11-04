// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using MessageSplitter.Contracts;
using Repositories.Contracts;

namespace MessageSplitter.Services
{
    public class MessageSplitterService : IMessageSplitterService
    {
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;

        public MessageSplitterService(IDatabaseSyncJobsRepository syncJobRespository)
        {
            _syncJobRepository = syncJobRespository ?? throw new ArgumentNullException(nameof(syncJobRespository));
        }

        public async Task UpdateJobStatusAsync(Guid jobId, Models.SyncStatus status)
        {
            var syncJob = await _syncJobRepository.GetSyncJobAsync(jobId);
            if (syncJob != null)
            {
                var currentDate = DateTime.UtcNow;
                syncJob.LastRunTime = currentDate;
                await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob }, status);
            }
        }
    }
}
