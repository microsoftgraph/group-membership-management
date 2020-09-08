// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks;

namespace Tests.Repositories
{
	class MockSGRepository : IGraphGroupRepository
	{
		private readonly Dictionary<AzureADGroup, IEnumerable<IAzureADObject>> _groupsToChildren;
		private readonly TimeSpan _delay;
		public MockSGRepository(Dictionary<AzureADGroup, IEnumerable<IAzureADObject>> groupsToChildren, TimeSpan delay)
		{
			_groupsToChildren = groupsToChildren;
			_delay = delay;
		}

		public Task AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
		{
			await Task.Delay(_delay);
			return _groupsToChildren[new AzureADGroup { ObjectId = objectId }];
		}

		public Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}
	}
}

