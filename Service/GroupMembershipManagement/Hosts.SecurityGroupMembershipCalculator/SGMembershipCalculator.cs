// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Entities.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.SecurityGroupMembershipCalculator
{
	public class SGMembershipCalculator
	{
		private readonly IGraphGroupRepository _graphGroupRepository;
		private readonly IMembershipServiceBusRepository _membershipServiceBus;

		public SGMembershipCalculator(IGraphGroupRepository graphGroupRepository, IMembershipServiceBusRepository membershipServiceBus)
		{
			_graphGroupRepository = graphGroupRepository;
			_membershipServiceBus = membershipServiceBus;
		}

		public async Task SendMembership(SyncJob syncJob)
		{
			// Query will be renamed to "argument" soon.
			var sourceGroups = syncJob.Query.Split(';').Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty)
				.Where(x => x != Guid.Empty)
				.Select(x => new AzureADGroup { ObjectId = x }).ToArray();

			await _membershipServiceBus.SendMembership(new GroupMembership
			{
				SourceMembers = await GetUsersForEachGroup(sourceGroups),
				Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
				Sources = sourceGroups,
				SyncJobRowKey = syncJob.RowKey,
				SyncJobPartitionKey = syncJob.PartitionKey
			});
		}

		private async Task<List<AzureADUser>> GetUsersForEachGroup(IEnumerable<AzureADGroup> groups)
		{
			var toReturn = new List<AzureADUser>();

			foreach (var group in groups)
			{
				toReturn.AddRange(await _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId));
			}

			return toReturn;
		}
	}
}

