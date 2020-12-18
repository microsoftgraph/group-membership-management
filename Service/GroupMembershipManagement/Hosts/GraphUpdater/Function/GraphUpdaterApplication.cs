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
		private readonly ILoggingRepository _log;
		private readonly IMailRepository _mailRepository;
		private readonly ILocalizationRepository _localizationRepository;

		public GraphUpdaterApplication(IMembershipDifferenceCalculator<AzureADUser> differenceCalculator, IGraphGroupRepository graphGroups, ISyncJobRepository syncJobRepository, ILoggingRepository logging, IMailRepository mailRepository, ILocalizationRepository localizationRepository)
		{
			_differenceCalculator = differenceCalculator;
			_graphGroups = graphGroups;
			_syncJobRepo = syncJobRepository;
			_log = logging;
			_mailRepository = mailRepository;
			_localizationRepository = localizationRepository;
		}

		public async Task CalculateDifference(GroupMembership membership)
		{
			_graphGroups.RunId = membership.RunId;
			var fromto = $"from {PrettyprintSources(membership.Sources)} to {membership.Destination}";
            _log.SyncJobProperties = new Dictionary<string, string>
            {
				{ "partitionKey", membership.SyncJobPartitionKey },
				{ "rowKey", membership.SyncJobRowKey },
				{ "targetOfficeGroupId", membership.Destination.ObjectId.ToString() }
			};
           
			var changeTo = await SynchronizeGroups(membership, fromto);

			var syncJobsBeingProcessed = _syncJobRepo.GetSyncJobsAsync(new[] { (membership.SyncJobPartitionKey, membership.SyncJobRowKey) });

			// should only be one sync job in here, doesn't hurt to iterate over "all" of them
			await foreach (var job in syncJobsBeingProcessed)
			{
				await _log.LogMessageAsync(new LogMessage { Message = $"syncJobsBeingProcessed is being processed as part of RunId: {job.RunId} ", RunId = membership.RunId });
				await _log.LogMessageAsync(new LogMessage { Message = $"{job.TargetOfficeGroupId} job's status is {job.Status}.", RunId = membership.RunId });

				bool isInitialSync = job.LastRunTime == DateTime.FromFileTimeUtc(0);
				job.LastRunTime = DateTime.UtcNow;
				job.RunId = membership.RunId;
				job.Enabled = changeTo.syncStatus == SyncStatus.Error ? false : job.Enabled; // disable the job if the destination group doesn't exist
				
				await _log.LogMessageAsync(new LogMessage { Message = $"Sync jobs being batched : Partition key {job.PartitionKey} , Row key {job.RowKey}", RunId = membership.RunId });
				await _syncJobRepo.UpdateSyncJobStatusAsync(new[] { job }, changeTo.syncStatus);
				
				if (isInitialSync)
                {
					var emailSubjectToRequestor = _localizationRepository.TranslateSetting("EmailSubject");
					var emailContentToRequestor = _localizationRepository.TranslateSetting("SyncCompletedEmailBody", job.TargetOfficeGroupId.ToString(), changeTo.AddCount.ToString(), changeTo.RemoveCount.ToString());
					await _mailRepository.SendMail(emailSubjectToRequestor, emailContentToRequestor, job.Requestor);
				}

				_ = _log.LogMessageAsync(new LogMessage { Message = $"Set job status to {changeTo.syncStatus}.", RunId = membership.RunId });
			}

			// this is a grasping-at-straws troubleshooting step
			await foreach (var job in syncJobsBeingProcessed)
			{
				await _log.LogMessageAsync(new LogMessage { Message = $"On another read, job's status is {job.Status}.", RunId = membership.RunId });

				if (job.Status == "InProgress")
				{
					await _log.LogMessageAsync(new LogMessage { Message = "Job is stuck in progress. Attempting to force it back to Idle.", RunId = membership.RunId });
					job.Status = "Idle";
					await _syncJobRepo.UpdateSyncJobStatusAsync(new[] { job }, SyncStatus.Idle);
					await _log.LogMessageAsync(new LogMessage { Message = $"Forced set job status to {job.Status}", RunId = membership.RunId });
				}
			}

			await _log.LogMessageAsync(new LogMessage { Message = $"Syncing {fromto} done.", RunId = membership.RunId });
		}

		private async Task<(SyncStatus syncStatus, int AddCount, int RemoveCount)> SynchronizeGroups(GroupMembership membership, string fromto)
		{
			if (membership.Errored)
			{
				await _log.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, calculator reported an error. Not syncing and marking as error.", RunId = membership.RunId });
				return (SyncStatus.Error, 0, 0);
			}

			// this gets checked for in the job trigger, but no harm in checking it here, too.
			if (await _graphGroups.GroupExists(membership.Destination.ObjectId))
			{
				var response = await DoSynchronization(membership, fromto);
				return (SyncStatus.Idle, response.AddCount, response.RemoveCount);
			}
			else
			{
				await _log.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, destination group {membership.Destination} doesn't exist. Not syncing and marking as error.", RunId = membership.RunId });
				return (SyncStatus.Error, 0, 0);
			}
		}

		private async Task<(int AddCount, int RemoveCount)> DoSynchronization(GroupMembership membership, string fromto)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"Calculating membership difference {fromto}.", RunId = membership.RunId });
			Stopwatch stopwatch = Stopwatch.StartNew();
			var delta = _differenceCalculator.CalculateDifference(membership.SourceMembers, await _graphGroups.GetUsersInGroupTransitively(membership.Destination.ObjectId));
			stopwatch.Stop();
			await _log.LogMessageAsync(new LogMessage { Message = $"Calculated membership difference {fromto} in {stopwatch.Elapsed.TotalSeconds} seconds. Adding {delta.ToAdd.Count} users and removing {delta.ToRemove.Count}.", RunId = membership.RunId });

			stopwatch.Restart();
			await _graphGroups.AddUsersToGroup(delta.ToAdd, membership.Destination);
			await _graphGroups.RemoveUsersFromGroup(delta.ToRemove, membership.Destination);
			stopwatch.Stop();
			await _log.LogMessageAsync(new LogMessage { Message = $"Synchronization {fromto} complete in {stopwatch.Elapsed.TotalSeconds} seconds. {delta.ToAdd.Count / stopwatch.Elapsed.TotalSeconds} users added per second. {delta.ToRemove.Count / stopwatch.Elapsed.TotalSeconds} users removed per second. Marking job as idle.", RunId = membership.RunId });
			return (delta.ToAdd.Count, delta.ToRemove.Count);
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
