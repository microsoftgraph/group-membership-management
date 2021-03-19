// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class SGMembershipCalculator
	{
		private readonly IGraphGroupRepository _graphGroupRepository;
		private readonly IMembershipServiceBusRepository _membershipServiceBus;
		private readonly ILoggingRepository _log;

		public SGMembershipCalculator(IGraphGroupRepository graphGroupRepository, IMembershipServiceBusRepository membershipServiceBus, ILoggingRepository logging)
		{
			_graphGroupRepository = graphGroupRepository;
			_membershipServiceBus = membershipServiceBus;
			_log = logging;
		}

		public async Task SendMembership(SyncJob syncJob)
		{
			_log.SyncJobProperties = syncJob.ToDictionary();
			var runId = syncJob.RunId.GetValueOrDefault(Guid.NewGuid());
			_graphGroupRepository.RunId = runId;

			var sourceGroups = syncJob.Query.Split(';').Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty)
				.Where(x => x != Guid.Empty)
				.Select(x => new AzureADGroup { ObjectId = x }).ToArray();

			await _log.LogMessageAsync(new LogMessage
			{
				RunId = runId,
				Message =
				$"Reading source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
			});

			if (sourceGroups.Length == 0)
			{
				await _log.LogMessageAsync(new LogMessage
				{
					RunId = runId,
					Message =
					$"None of the source groups in {syncJob.Query} were valid guids."
				});
			}

			var allusers = await GetUsersForEachGroup(sourceGroups, runId);

			if (allusers != null)
			{
				await _log.LogMessageAsync(new LogMessage
				{
					RunId = runId,
					Message =
					$"Read {allusers.Count} users from source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
				});
			}
			else
			{
				await _log.LogMessageAsync(new LogMessage
				{
					RunId = runId,
					Message =
					$"At least one group in {syncJob.Query} does not exist. Not syncing and marking job as errored."
				});
			}

			await _membershipServiceBus.SendMembership(new GroupMembership
			{
				SourceMembers = allusers ?? new List<AzureADUser>(),
				Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
				Sources = sourceGroups,
				RunId = runId,
				SyncJobRowKey = syncJob.RowKey,
				SyncJobPartitionKey = syncJob.PartitionKey,
				Errored = allusers == null
			});

			await _log.LogMessageAsync(new LogMessage
			{
				RunId = runId,
				Message = allusers != null ?
				$"Successfully sent {allusers.Count} users from source groups {syncJob.Query} to GraphUpdater to be put into the destination group {syncJob.TargetOfficeGroupId}." :
				$"Sync job errored out trying to read from source groups {syncJob.Query}."
			});
		}

		private Task<List<AzureADUser>> GetUsersInGroupWithRetry(AzureADGroup group, Guid runId)
		{
			const int number_of_retries = 5;
			for (int i = 0; i < number_of_retries; i++)
			{
				try
				{
					return _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId);
				}
				catch (SocketException ex)
				{
					_ = _log.LogMessageAsync(new LogMessage
					{
						// i + 1 because when i is 0, we're on our first try, and so on
						Message = $"Got a transient SocketException. Retrying GetUsersInGroupTransitively(). This was try {i + 1} out of {number_of_retries}.\n" + ex.ToString(),
						RunId = runId
					});
				}
			}

			_ = _log.LogMessageAsync(new LogMessage
			{
				Message = $"GetUsersInGroupTransitively() failed after {number_of_retries} attemps. Marking job as errored.",
				RunId = runId
			});

			return null;
		}

		private async Task<List<AzureADUser>> GetUsersForEachGroup(IEnumerable<AzureADGroup> groups, Guid runId)
		{
			if (!groups.Any()) { return null; }

			var toReturn = new List<AzureADUser>();

			foreach (var group in groups)
			{
				if (await _graphGroupRepository.GroupExists(group.ObjectId))
				{
					await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Reading users from the group with ID {group.ObjectId}." });
					var users = await GetUsersInGroupWithRetry(group, runId);
					if (users == null) { return null; }
					var newUsers = users.Except(toReturn).ToArray();
					await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Got {users.Count} users from the group with ID {group.ObjectId}. " +
						$"The group contains {users.Count - newUsers.Length} users who have already been read from earlier groups." });
					toReturn.AddRange(newUsers);
				}
				else
				{
					await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Group with ID {group.ObjectId} doesn't exist. Stopping sync and marking as error." });
					return null;
				}
			}

			return toReturn;
		}
	}
}
