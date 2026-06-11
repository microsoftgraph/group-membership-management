// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using MessageSplitter.Contracts;
using Repositories.Contracts;
using Services.Contracts;
using Models.SyncJobHistory;

namespace MessageSplitter.Services
{
    public class MessageSplitterService : IMessageSplitterService
    {
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public MessageSplitterService(IDatabaseSyncJobsRepository syncJobRespository, ISyncJobStatusService syncJobStatusService)
        {
            _syncJobRepository = syncJobRespository ?? throw new ArgumentNullException(nameof(syncJobRespository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        public async Task UpdateJobStatusAsync(Guid jobId, Models.SyncStatus status)
        {
            var syncJob = await _syncJobRepository.GetSyncJobAsync(jobId);
            if (syncJob != null)
            {
                var currentDate = DateTime.UtcNow;
                syncJob.LastRunTime = currentDate;
                var history = new SyncJobHistory
                {
                    SyncJobId = syncJob.Id,
                    RunId = syncJob.RunId ?? Guid.Empty,
                    Status = status.ToString(),
                    UpdatedByFunction = "MessageSplitter",
                    EndTime = status != Models.SyncStatus.InProgress ? currentDate : (DateTime?)null,
                    UpdatedAt = currentDate
                };
                await _syncJobStatusService.UpdateJobStatusAsync(syncJob, status, history, functionName: "MessageSplitter");
            }
        }
    }
}
