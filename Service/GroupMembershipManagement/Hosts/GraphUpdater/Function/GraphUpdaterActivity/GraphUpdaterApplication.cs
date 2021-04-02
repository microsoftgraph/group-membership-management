// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
	public class GraphUpdaterApplication : IGraphUpdater
	{
		private const string EmailSubject = "EmailSubject";
		private const string SyncCompletedEmailBody = "SyncCompletedEmailBody";
		private const string SyncDisabledEmailBody = "SyncDisabledEmailBody";

		private readonly IMembershipDifferenceCalculator<AzureADUser> _differenceCalculator;
		private readonly IGraphGroupRepository _graphGroups;
		private readonly ISyncJobRepository _syncJobRepo;
		private readonly ILoggingRepository _log;
		private readonly IMailRepository _mailRepository;
		private readonly IGraphGroupRepository _graphGroupRepository;
		private readonly IEmailSenderRecipient _emailSenderAndRecipients;

		public GraphUpdaterApplication(
			IMembershipDifferenceCalculator<AzureADUser> differenceCalculator,
			IGraphGroupRepository graphGroups,
			ISyncJobRepository syncJobRepository,
			ILoggingRepository logging,
			IMailRepository mailRepository,
			IGraphGroupRepository graphGroupRepository,
			IEmailSenderRecipient emailSenderAndRecipients
			)
		{
			_emailSenderAndRecipients = emailSenderAndRecipients;
			_differenceCalculator = differenceCalculator;
			_graphGroups = graphGroups;
			_syncJobRepo = syncJobRepository;
			_log = logging;
			_mailRepository = mailRepository;
			_graphGroupRepository = graphGroupRepository;
		}

		public async Task CalculateDifference(GroupMembership membership)
		{
			_graphGroups.RunId = membership.RunId;
			var fromto = $"from {PrettyPrintSources(membership.Sources)} to {membership.Destination}";
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
				var groupName = await _graphGroupRepository.GetGroupNameAsync(job.TargetOfficeGroupId);

				await _log.LogMessageAsync(new LogMessage { Message = $"syncJobsBeingProcessed is being processed as part of RunId: {job.RunId} ", RunId = membership.RunId });
				await _log.LogMessageAsync(new LogMessage { Message = $"{job.TargetOfficeGroupId} job's status is {job.Status}.", RunId = membership.RunId });

				var isInitialSync = job.LastRunTime == DateTime.FromFileTimeUtc(0);
				job.LastRunTime = DateTime.UtcNow;
				job.RunId = membership.RunId;
				if (changeTo.syncStatus == SyncStatus.Error)
				{
					job.Enabled = false;
				}
				await _log.LogMessageAsync(new LogMessage { Message = $"Sync jobs being batched : Partition key {job.PartitionKey} , Row key {job.RowKey}", RunId = membership.RunId });
				await _syncJobRepo.UpdateSyncJobStatusAsync(new[] { job }, changeTo.syncStatus);

				if (isInitialSync && job.Status == SyncStatus.Idle.ToString())
				{
					var message = new EmailMessage
					{
						Subject = EmailSubject,
						Content = SyncCompletedEmailBody,
						SenderAddress = _emailSenderAndRecipients.SenderAddress,
						SenderPassword = _emailSenderAndRecipients.SenderPassword,
						ToEmailAddresses = job.Requestor,
						CcEmailAddresses = _emailSenderAndRecipients.SyncCompletedCCAddresses,
						AdditionalContentParams = new[] { groupName, job.TargetOfficeGroupId.ToString(), changeTo.AddMembersCount.ToString(), changeTo.RemoveMembersCount.ToString() }
					};

					await _mailRepository.SendMailAsync(message);
				}
				if (job.Status == SyncStatus.Error.ToString())
				{
					var message = new EmailMessage
					{
						Subject = EmailSubject,
						Content = SyncDisabledEmailBody,
						SenderAddress = _emailSenderAndRecipients.SenderAddress,
						SenderPassword = _emailSenderAndRecipients.SenderPassword,
						ToEmailAddresses = job.Requestor,
						CcEmailAddresses = _emailSenderAndRecipients.SyncDisabledCCAddresses,
						AdditionalContentParams = new[] { PrettyPrintSources(membership.Sources) }
					};
					await _mailRepository.SendMailAsync(message);
				}

				await _log.LogMessageAsync(new LogMessage { Message = $"Set job status to {changeTo.syncStatus}.", RunId = membership.RunId });
			}

			await _log.LogMessageAsync(new LogMessage { Message = $"Syncing {fromto} done.", RunId = membership.RunId });
		}

		private async Task<(SyncStatus syncStatus, int AddMembersCount, int RemoveMembersCount)> SynchronizeGroups(GroupMembership membership, string fromto)
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
				return (SyncStatus.Idle, response.AddMembersCount, response.RemoveMembersCount);
			}
			else
			{
				await _log.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, destination group {membership.Destination} doesn't exist. Not syncing and marking as error.", RunId = membership.RunId });
				return (SyncStatus.Error, 0, 0);
			}
		}

		private async Task<(int AddMembersCount, int RemoveMembersCount)> DoSynchronization(GroupMembership membership, string fromto)
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

		private string PrettyPrintSources(AzureADGroup[] sources)
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
