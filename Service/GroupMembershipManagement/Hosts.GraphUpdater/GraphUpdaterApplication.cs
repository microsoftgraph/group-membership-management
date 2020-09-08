// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Entities.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
	public class GraphUpdaterApplication : IGraphUpdater
	{
		private readonly IMembershipDifferenceCalculator<AzureADUser> _differenceCalculator;
		private readonly IGraphGroupRepository _graphGroups;
		private readonly ISyncJobRepository _syncJobRepo;

		public GraphUpdaterApplication(IMembershipDifferenceCalculator<AzureADUser> differenceCalculator, IGraphGroupRepository graphGroups, ISyncJobRepository syncJobRepository)
		{
			_differenceCalculator = differenceCalculator;
			_graphGroups = graphGroups;
			_syncJobRepo = syncJobRepository;
		}

		public async Task CalculateDifference(GroupMembership membership)
		{
			//Console.WriteLine($"Calculating difference in membership between {string.Join(',', membership.Sources.AsEnumerable())} and {membership.Destination}");
			Stopwatch stopwatch = Stopwatch.StartNew();
			var delta = _differenceCalculator.CalculateDifference(membership.SourceMembers, await _graphGroups.GetUsersInGroupTransitively(membership.Destination.ObjectId));
			stopwatch.Stop();
			//Console.WriteLine($"Calulated difference in {stopwatch.Elapsed.TotalSeconds} seconds. Adding {delta.ToAdd.Count} users and removing {delta.ToRemove.Count}.");

			stopwatch = Stopwatch.StartNew();
			await _graphGroups.AddUsersToGroup(delta.ToAdd, membership.Destination);
			await _graphGroups.RemoveUsersFromGroup(delta.ToRemove, membership.Destination);
			stopwatch.Stop();
			//Console.WriteLine($"Synchronization complete in {stopwatch.Elapsed.TotalSeconds} seconds.");
			//Console.WriteLine($"{delta.ToAdd.Count/stopwatch.Elapsed.TotalSeconds} users added per second.");
			//Console.WriteLine($"{delta.ToRemove.Count/stopwatch.Elapsed.TotalSeconds} users removed per second.");

			//Console.WriteLine($"Marking the sync job ({membership.SyncJobPartitionKey}, {membership.SyncJobRowKey}) as idle.");

			// should only be one sync job in here, doesn't hurt to iterate over "all" of them
			await foreach (var job in _syncJobRepo.GetSyncJobsAsync(new[] { (membership.SyncJobPartitionKey, membership.SyncJobRowKey) }))
			{
				job.LastRunTime = DateTime.UtcNow;
				await _syncJobRepo.UpdateSyncJobStatusAsync(new[] { job }, SyncStatus.Idle);
			}
		}
	}

	public interface IGraphUpdater
	{
		Task CalculateDifference(GroupMembership groupMembership);

	}
}

