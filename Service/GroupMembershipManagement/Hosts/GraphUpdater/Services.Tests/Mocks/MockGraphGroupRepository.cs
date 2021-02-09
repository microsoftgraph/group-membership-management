// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services.Tests.Mocks
{
	class MockGraphGroupRepository : IGraphGroupRepository
	{
		public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; } = new Dictionary<Guid, List<AzureADUser>>();
		public Guid RunId { get; set; }

		public Task AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			GroupsToUsers[targetGroup.ObjectId].AddRange(users);
			return Task.CompletedTask;
		}

		public Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
		{
			return Task.FromResult(GroupsToUsers[objectId].Cast<IAzureADObject>());
		}

		public Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
		{
			return Task.FromResult(GroupsToUsers[objectId]);
		}

		public Task<string> GetGroupNameAsync(Guid objectId)
		{
			return Task.FromResult("GroupName");
		}

		public Task<bool> GroupExists(Guid objectId)
		{
			return Task.FromResult(GroupsToUsers.Keys.Contains(objectId));
		}

		public Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
		{
			throw new NotImplementedException();
		}

		public Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			GroupsToUsers[targetGroup.ObjectId].RemoveAll(x => users.Contains(x));
			return Task.CompletedTask;
		}
	}
}
