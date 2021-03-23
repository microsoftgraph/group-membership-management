// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
	class MockGraphGroupRepository : IGraphGroupRepository
	{
		public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; }
		public int ThrowSocketExceptionsFromGroupExistsBeforeSuccess { get; set; } = 0;
		public bool ThrowNonSocketExceptionFromGroupExists { get; set; } = false;
		public int ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess { get; set; } = 0;
		public bool ThrowNonSocketExceptionFromGetUsersInGroup { get; set; } = false;
		public Guid RunId { get; set; }

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
			if (ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess > 0)
			{
				ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess--;
				throw new SocketException();
			}
			if (ThrowNonSocketExceptionFromGetUsersInGroup) { throw new Exception("This should be handled gracefully."); }
			return Task.FromResult(GroupsToUsers[objectId]);
		}

		public Task<bool> GroupExists(Guid objectId)
		{
			if (ThrowSocketExceptionsFromGroupExistsBeforeSuccess > 0)
			{
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess--;
				throw new SocketException();
			}
			if (ThrowNonSocketExceptionFromGroupExists) { throw new Exception("This should be handled gracefully."); }
			return Task.FromResult(GroupsToUsers.ContainsKey(objectId));
		}

		public Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
		{
			throw new NotImplementedException();
		}

		public Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}
	}
}
