using Entities;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services
{
    public class SyncJobTopicsService : ISyncJobTopicService
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository;
		private readonly IGraphGroupRepository _graphGroupRepository;

		public SyncJobTopicsService(
            ILoggingRepository loggingRepository,
            ISyncJobRepository syncJobRepository,
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            IGraphGroupRepository graphGroupRepository
            )
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
            _graphGroupRepository = graphGroupRepository  ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        public async Task ProcessSyncJobsAsync()
        {
            var jobs = _syncJobRepository.GetSyncJobsAsync(SyncStatus.Idle);

            var runId = Guid.NewGuid();
            var runningJobs = new List<SyncJob>();
            var failedJobs = new List<SyncJob>();
            var startedTasks = new List<Task>();
            await foreach (var job in jobs)
            {
                job.RunId = runId;
                var _ = _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    RunId = runId,
                    Message = $"Job RunId: {job.RunId}, Partition Key: {job.PartitionKey}, Row Key: {job.RowKey}"
                });

                if (await _graphGroupRepository.GroupExists(job.TargetOfficeGroupId))
				{
					_ = _loggingRepository.LogMessageAsync(new LogMessage
					{
						RunId = runId,
						Message = $"Starting job. Query: {job.Query} Sync Type: {job.Type} Job RunId: {job.RunId}, Partition Key: {job.PartitionKey}, Row Key: {job.RowKey}"
					});
					startedTasks.Add(_serviceBusTopicsRepository.AddMessageAsync(job));
					runningJobs.Add(job);
				}
				else
				{
					_ = _loggingRepository.LogMessageAsync(new LogMessage
					{
						RunId = runId,
						Message = $"Marking sync job as failed because destination group {job.TargetOfficeGroupId} doesn't exist. Job RunId: {job.RunId}, Partition Key: {job.PartitionKey}, Row Key: {job.RowKey}"
					});
					job.Enabled = false;
					failedJobs.Add(job);
				}
			}
			startedTasks.Add(_syncJobRepository.UpdateSyncJobStatusAsync(runningJobs, SyncStatus.InProgress));
			startedTasks.Add(_syncJobRepository.UpdateSyncJobStatusAsync(failedJobs, SyncStatus.Error));
			await Task.WhenAll(startedTasks);
		}
	}
}
