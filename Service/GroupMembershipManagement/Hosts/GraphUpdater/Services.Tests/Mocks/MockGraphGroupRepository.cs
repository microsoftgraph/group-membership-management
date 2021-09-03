// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Graph;
using System.Collections;

namespace Services.Tests.Mocks
{
	class MockGraphGroupRepository : IGraphGroupRepository
	{
		public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; } = new Dictionary<Guid, List<AzureADUser>>();
		public Guid RunId { get; set; }

		public Task<(ResponseCode ResponseCode, int SuccessCount)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			GroupsToUsers[targetGroup.ObjectId].AddRange(users);
			return Task.FromResult((ResponseCode.Ok, users.ToList().Count));
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

		public Task<(ResponseCode ResponseCode, int SuccessCount)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			GroupsToUsers[targetGroup.ObjectId].RemoveAll(x => users.Contains(x));
			return Task.FromResult((ResponseCode.Ok, users.ToList().Count));
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
			throw new NotImplementedException();
		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
		{
			throw new NotImplementedException();
		}
    }
}
