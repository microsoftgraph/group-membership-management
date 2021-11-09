// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graph;

namespace Repositories.SecurityGroupCrawler.Tests
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

		public Guid RunId { get; set; }

		public Task<(ResponseCode ResponseCode, int SuccessCount)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public Task<string> GetGroupNameAsync(Guid objectId)
		{
			return Task.FromResult("GroupName");
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

		public Task<bool> GroupExists(Guid objectId)
		{
			return Task.FromResult(_groupsToChildren.ContainsKey(new AzureADGroup { ObjectId = objectId }));
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
			throw new NotImplementedException();
		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
		{
			throw new NotImplementedException();
		}

        public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            throw new NotImplementedException();
        }

        public Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            throw new NotImplementedException();
        }
    }
}
