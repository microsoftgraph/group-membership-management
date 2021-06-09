// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockSyncJobRepository : ISyncJobRepository
	{
		public Dictionary<(string, string), SyncJob> ExistingSyncJobs = new Dictionary<(string, string), SyncJob>();

		public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
		{
			var job = ExistingSyncJobs.ContainsKey((partitionKey, rowKey)) ? ExistingSyncJobs[(partitionKey, rowKey)] : null;
			return await Task.FromResult(job);
		}

		// these aren't actually async, but this is the easiest way to get these to return IAsyncEnumerables
        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool includeDisabled = false)
		{
			foreach (var job in ExistingSyncJobs.Values.Where(x => Enum.Parse<SyncStatus>(x.Status) == status || status == SyncStatus.All).Where(x => includeDisabled && x.Enabled))
				yield return job;
		}

		public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds)
		{
			foreach (var job in jobIds.Select(x => ExistingSyncJobs[x]))
				yield return job;
		}

		public Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status)
		{
			foreach (var job in jobs)
				job.Status = status.ToString();
			return Task.CompletedTask;
		}
	}
}
