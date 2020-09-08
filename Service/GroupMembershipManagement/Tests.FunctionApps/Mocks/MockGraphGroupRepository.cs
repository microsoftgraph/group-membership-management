// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
	class MockGraphGroupRepository : IGraphGroupRepository
	{
		public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; }

		public Task AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
		{
			return Task.FromResult(GroupsToUsers[objectId]);
		}

		public Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}
	}
}

