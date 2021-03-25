// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests.Mocks
{
    class MockSyncJobRepository : ISyncJobRepository
	{
		public Dictionary<(string, string), SyncJob> ExistingSyncJobs = new Dictionary<(string, string), SyncJob>();

		public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
		{
			var job = ExistingSyncJobs.ContainsKey((partitionKey, rowKey)) ? ExistingSyncJobs[(partitionKey, rowKey)] : null;
			return await Task.FromResult(job);
		}

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
