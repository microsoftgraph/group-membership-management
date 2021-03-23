// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Polly;
using Polly.Retry;
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

		private const int NumberOfGraphRetries = 5;
		private AsyncRetryPolicy _graphRetryPolicy;
		public async Task SendMembership(SyncJob syncJob)
		{
			_log.SyncJobProperties = syncJob.ToDictionary();
			var runId = syncJob.RunId.GetValueOrDefault(Guid.NewGuid());
			_graphGroupRepository.RunId = runId;

			// make this fresh every time because the lambda has to capture the run ID
			_graphRetryPolicy = Policy.Handle<SocketException>().WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
				   onRetry: async (ex, count) =>
				   {
					   await _log.LogMessageAsync(new LogMessage
					   {
						   Message = $"Got a transient SocketException. Retrying. This was try {count} out of {NumberOfGraphRetries}.\n" + ex.ToString(),
						   RunId = runId
					   });
				   });


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

			List<AzureADUser> allusers;
			try
			{
				allusers = await GetUsersForEachGroup(sourceGroups, runId);
			}
			catch (Exception ex)
			{
				await _log.LogMessageAsync(new LogMessage
				{
					Message = "Caught unexpected exception, marking sync job as errored. Exception:\n" + ex,
					RunId = runId
				});
				allusers = null;
			}

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
					$"Something went wrong while trying to read users from groups. Not syncing and marking job as errored."
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

		private async Task<List<AzureADUser>> GetUsersInGroupWithRetry(AzureADGroup group, Guid runId)
		{
			var result = await _graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId));

			if (result.Outcome == OutcomeType.Failure)
			{
				await _log.LogMessageAsync(new LogMessage
				{
					Message = $"GetUsersInGroupTransitively() failed after {NumberOfGraphRetries} attempts. Marking job as errored.",
					RunId = runId
				});
				return null;
			}

			return result.Result;
		}


		private async Task<List<AzureADUser>> GetUsersForEachGroup(IEnumerable<AzureADGroup> groups, Guid runId)
		{
			if (!groups.Any()) { return null; }

			var toReturn = new List<AzureADUser>();

			foreach (var group in groups)
			{
				var groupExistsResult = await _graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(group.ObjectId));
				if (groupExistsResult.Outcome == OutcomeType.Successful && groupExistsResult.Result)
				{
					await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Reading users from the group with ID {group.ObjectId}." });
					var users = await GetUsersInGroupWithRetry(group, runId);
					if (users == null) { return null; }
					var newUsers = users.Except(toReturn).ToArray();
					await _log.LogMessageAsync(new LogMessage
					{
						RunId = runId,
						Message = $"Got {users.Count} users from the group with ID {group.ObjectId}. " +
						$"The group contains {users.Count - newUsers.Length} users who have already been read from earlier groups."
					});
					toReturn.AddRange(newUsers);
				}
				else
				{
					if (groupExistsResult.Outcome == OutcomeType.Successful)
						await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Group with ID {group.ObjectId} doesn't exist. Stopping sync and marking as error." });
					else
						await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Exceeded {NumberOfGraphRetries} while trying to determine if a group exists. Stopping sync and marking as error." });
					return null;
				}
			}

			return toReturn;
		}
	}
}
