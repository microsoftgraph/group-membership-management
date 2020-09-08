using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services.Tests.Mocks
{
	class MockSyncJobRepository : ISyncJobRepository
	{
		public Dictionary<(string, string), SyncJob> ExistingSyncJobs = new Dictionary<(string, string), SyncJob>();

		public IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool includeDisabled = false)
		{
			return ExistingSyncJobs.Values.Where(x => Enum.Parse<SyncStatus>(x.Status) == status || status == SyncStatus.All).Where(x => includeDisabled && x.Enabled).ToAsyncEnumerable();
		}

		public IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds)
		{
			return jobIds.Select(x => ExistingSyncJobs[x]).ToAsyncEnumerable();
		}

		public Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status)
		{
			foreach (var job in jobs)
				job.Status = status.ToString();
			return Task.CompletedTask;
		}
	}
}
