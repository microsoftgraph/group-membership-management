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
		private readonly ILoggingRepository _log;

		public GraphUpdaterApplication(IMembershipDifferenceCalculator<AzureADUser> differenceCalculator, IGraphGroupRepository graphGroups, ISyncJobRepository syncJobRepository, ILoggingRepository logging)
		{
			_differenceCalculator = differenceCalculator;
			_graphGroups = graphGroups;
			_syncJobRepo = syncJobRepository;
			_log = logging;
		}

		public async Task CalculateDifference(GroupMembership membership)
		{
			_graphGroups.RunId = membership.RunId;
			var fromto = $"from {PrettyprintSources(membership.Sources)} to {membership.Destination}";
			_log.SyncJobInfo = $" SyncJobRowKey: {membership.SyncJobRowKey}. SyncJobPartitionKey: {membership.SyncJobPartitionKey}";

			SyncStatus changeTo = await SynchronizeGroups(membership, fromto);

			// should only be one sync job in here, doesn't hurt to iterate over "all" of them
			await foreach (var job in _syncJobRepo.GetSyncJobsAsync(new[] { (membership.SyncJobPartitionKey, membership.SyncJobRowKey) }))
			{
				job.LastRunTime = DateTime.UtcNow;
				job.Enabled = changeTo == SyncStatus.Error ? false : job.Enabled; // disable the job if the destination group doesn't exist
				await _syncJobRepo.UpdateSyncJobStatusAsync(new[] { job }, changeTo);
			}

			_ = _log.LogMessageAsync(new LogMessage { Message = $"Syncing {fromto} done.", RunId = membership.RunId });
		}

		private async Task<SyncStatus> SynchronizeGroups(GroupMembership membership, string fromto)
		{
			if (membership.Errored)
			{
				await _log.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, calculator reported an error. Not syncing and marking as error.", RunId = membership.RunId });
				return SyncStatus.Error;
			}

			// this gets checked for in the job trigger, but no harm in checking it here, too.
			if (await _graphGroups.GroupExists(membership.Destination.ObjectId))
			{
				await DoSynchronization(membership, fromto);
				return SyncStatus.Idle;
			}
			else
			{
				await _log.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, destination group {membership.Destination} doesn't exist. Not syncing and marking as error.", RunId = membership.RunId });
				return SyncStatus.Error;
			}
		}

		private async Task DoSynchronization(GroupMembership membership, string fromto)
		{
			var _ = _log.LogMessageAsync(new LogMessage { Message = $"Calculating membership difference {fromto}.", RunId = membership.RunId });
			Stopwatch stopwatch = Stopwatch.StartNew();
			var delta = _differenceCalculator.CalculateDifference(membership.SourceMembers, await _graphGroups.GetUsersInGroupTransitively(membership.Destination.ObjectId));
			stopwatch.Stop();
			_ = _log.LogMessageAsync(new LogMessage { Message = $"Calculated membership difference {fromto} in {stopwatch.Elapsed.TotalSeconds} seconds. Adding {delta.ToAdd.Count} users and removing {delta.ToRemove.Count}.", RunId = membership.RunId });

			stopwatch.Restart();
			await _graphGroups.AddUsersToGroup(delta.ToAdd, membership.Destination);
			await _graphGroups.RemoveUsersFromGroup(delta.ToRemove, membership.Destination);
			stopwatch.Stop();
			_ = _log.LogMessageAsync(new LogMessage { Message = $"Synchronization {fromto} complete in {stopwatch.Elapsed.TotalSeconds} seconds. {delta.ToAdd.Count / stopwatch.Elapsed.TotalSeconds} users added per second. {delta.ToRemove.Count / stopwatch.Elapsed.TotalSeconds} users removed per second. Marking job as idle.", RunId = membership.RunId });
		}

		private string PrettyprintSources(AzureADGroup[] sources)
		{
			if (sources == null || sources.Length == 0)
				return "a non-security group sync";
			return string.Join(',', sources.AsEnumerable());
		}
	}

	public interface IGraphUpdater
	{
		Task CalculateDifference(GroupMembership groupMembership);
	}
}
