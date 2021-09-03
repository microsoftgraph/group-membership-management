// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
	class MockGraphGroupRepository : IGraphGroupRepository
	{
		public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; }
		public Dictionary<string, int> nonUserGraphObjects { get; set; }
		public IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup { get; set; }
		public int ThrowSocketExceptionsFromGroupExistsBeforeSuccess { get; set; } = 0;
		public bool ThrowNonSocketExceptionFromGroupExists { get; set; } = false;
		public int ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess { get; set; } = 0;
		public bool ThrowNonSocketExceptionFromGetUsersInGroup { get; set; } = false;
		public Guid RunId { get; set; }
		IGroupTransitiveMembersCollectionWithReferencesPage UsersFromPage { get; set; }

		public Task<(ResponseCode ResponseCode, int SuccessCount)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
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
			if (ThrowNonSocketExceptionFromGetUsersInGroup) { throw new MockException(); }
			return Task.FromResult(GroupsToUsers[objectId]);
		}

		public Task<bool> GroupExists(Guid objectId)
		{
			if (ThrowSocketExceptionsFromGroupExistsBeforeSuccess > 0)
			{
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess--;
				throw new SocketException();
			}
			if (ThrowNonSocketExceptionFromGroupExists) { throw new MockException(); }
			return Task.FromResult(GroupsToUsers.ContainsKey(objectId));
		}

		public Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
		{
			throw new NotImplementedException();
		}

		public Task<(ResponseCode ResponseCode, int SuccessCount)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersPageByIdAsync(string groupId)
		{
			throw new NotImplementedException();
		}

		public Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersNextPageAsnyc(IGroupTransitiveMembersCollectionWithReferencesPage groupMembersRef, string nextPageUrl)
		{
			throw new NotImplementedException();
		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstUsersPageAsync(Guid objectId)
		{
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            return Task.FromResult((users, nonUserGraphObjects, "", (IGroupTransitiveMembersCollectionWithReferencesPage)null));

		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
		{
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            return Task.FromResult((users, nonUserGraphObjects, "", (IGroupTransitiveMembersCollectionWithReferencesPage)null));
		}
	}

	public class MockException : Exception { }

}
