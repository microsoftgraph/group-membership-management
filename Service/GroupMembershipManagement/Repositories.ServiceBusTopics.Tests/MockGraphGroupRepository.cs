// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.ServiceBusTopics.Tests
{
	public class MockGraphGroupRepository : IGraphGroupRepository
	{
		public Guid RunId { get; set; }

		public HashSet<Guid> GroupsThatExist = new HashSet<Guid>();
		public HashSet<Guid> GroupsGMMOwns = new HashSet<Guid>();

		public Task AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public Task<string> GetGroupNameAsync(Guid objectId)
		{
			return Task.FromResult("GroupName");
		}

		public Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task<bool> GroupExists(Guid objectId)
		{
			return Task.FromResult(GroupsThatExist.Contains(objectId));
		}

		public Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
		{
			return Task.FromResult(GroupsGMMOwns.Contains(groupObjectId));
		}
	}
}
