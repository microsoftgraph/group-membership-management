// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
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

		public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
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

		public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
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
		public Task<IGroupDeltaCollectionPage> GetGroupUsersPageByIdAsync(string groupId)
		{
			throw new NotImplementedException();
		}
		public Task<IGroupDeltaCollectionPage> GetGroupUsersNextPageAsnyc(IGroupDeltaCollectionPage groupMembersRef, string nextPageUrl)
		{
			throw new NotImplementedException();
		}
		public Task<IGroupDeltaCollectionPage> GetGroupUsersPageByLinkAsync(string deltaLink)
		{
			throw new NotImplementedException();
		}
		public Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetFirstDeltaUsersPageAsync(string deltaLink)
		{
			throw new NotImplementedException();
		}
		public Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetNextDeltaUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage usersFromGroup)
		{
			throw new NotImplementedException();
		}
		public Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetFirstUsersPageAsync(Guid objectId)
		{
			throw new NotImplementedException();
		}
		public Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage usersFromGroup)
		{
			throw new NotImplementedException();
		}
		public Task<int> GetGroupsCountAsync(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstTransitiveMembersPageAsync(Guid objectId)
		{
			var users = new List<AzureADUser>();
			var nonUserGraphObjects = new Dictionary<string, int>();
			return Task.FromResult((users, nonUserGraphObjects, "", (IGroupTransitiveMembersCollectionWithReferencesPage)null));

		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextTransitiveMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
		{
			var users = new List<AzureADUser>();
			var nonUserGraphObjects = new Dictionary<string, int>();
			return Task.FromResult((users, nonUserGraphObjects, "", (IGroupTransitiveMembersCollectionWithReferencesPage)null));
		}

		public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
		{
			throw new NotImplementedException();
		}

		public Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
		{
			throw new NotImplementedException();
		}

		public Task<bool> GroupExists(string groupName)
		{
			throw new NotImplementedException();
		}

		public Task<AzureADGroup> GetGroup(string groupName)
		{
			throw new NotImplementedException();
		}

		public Task CreateGroup(string newGroupName)
		{
			throw new NotImplementedException();
		}

		public Task<List<AzureADUser>> GetTenantUsers(int userCount)
		{
			throw new NotImplementedException();
		}
	}

	public class MockException : Exception { }

}
