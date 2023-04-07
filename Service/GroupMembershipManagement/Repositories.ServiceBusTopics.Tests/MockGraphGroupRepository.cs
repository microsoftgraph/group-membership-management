// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graph;
using Models;

namespace Repositories.ServiceBusTopics.Tests
{
    public class MockGraphGroupRepository : IGraphGroupRepository
	{
		public Guid RunId { get; set; }

		public HashSet<Guid> GroupsThatExist = new HashSet<Guid>();
		public HashSet<Guid> GroupsGMMOwns = new HashSet<Guid>();

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
			throw new NotImplementedException();
		}

		public Task<bool> GroupExists(Guid objectId)
		{
			return Task.FromResult(GroupsThatExist.Contains(objectId));
		}

		public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			throw new NotImplementedException();
		}

		public Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
		{
			return Task.FromResult(GroupsGMMOwns.Contains(groupObjectId));
		}

		public Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersPageByIdAsync(string groupId)
		{
			throw new NotImplementedException();
		}

		public Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersNextPageAsnyc(IGroupTransitiveMembersCollectionWithReferencesPage groupMembersRef, string nextPageUrl)
		{
			throw new NotImplementedException();
		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstTransitiveMembersPageAsync(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextTransitiveMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
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
		public Task<IGraphServiceUsersCollectionPage> GetFirstMembersAsync(string url)
		{
			throw new NotImplementedException();
		}
        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGraphServiceUsersCollectionPage usersFromGroup)> GetFirstMembersPageAsync(string url)
		{
			throw new NotImplementedException();
		}
        public Task<IGraphServiceUsersCollectionPage> GetNextMembersAsync(IGraphServiceUsersCollectionPage groupMembersRef, string nextPageUrl)
		{
			throw new NotImplementedException();
		}
        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGraphServiceUsersCollectionPage usersFromGroup)> GetNextMembersPageAsync(string nextPageUrl, IGraphServiceUsersCollectionPage usersFromGroup)
		{
			throw new NotImplementedException();
		}
		public Task<IGraphServicePlacesCollectionPage> GetRoomsAsync(string url, int top, int skip)
		{
			throw new NotImplementedException();
		}
		public Task<(List<AzureADUser> users, IGraphServicePlacesCollectionPage usersFromGroup)> GetRoomsPageAsync(string url, int top, int skip)
		{
			throw new NotImplementedException();
		}
        public Task<IGraphServicePlacesCollectionPage> GetWorkSpacesAsync(string url, int top, int skip)
		{
			throw new NotImplementedException();
		}
        public Task<(List<AzureADUser> users, IGraphServicePlacesCollectionPage usersFromGroup)> GetWorkSpacesPageAsync(string url, int top, int skip)
		{
			throw new NotImplementedException();
		}
		public Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage usersFromGroup)
		{
			throw new NotImplementedException();
		}
		public Task<User> GetUserByEmail(string emailAddress)
		{
			throw new NotImplementedException();
		}
		public Task<int> GetGroupsCountAsync(Guid objectId)
		{
			throw new NotImplementedException();
		}
		public Task<int> GetUsersCountAsync(Guid objectId)
		{
			throw new NotImplementedException();
		}

		public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            throw new NotImplementedException();
        }

        public Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            var owners = new List<User>();
            for (var i = 0; i < 10; i++)
            {
                owners.Add(new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Mail = $"user{i}@mydomain.com"
                });
            }

            return Task.FromResult(owners);
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

		public Task<List<string>> GetGroupEndpointsAsync(Guid groupId)
		{
			throw new NotImplementedException();
		}

        public Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds)
        {
            throw new NotImplementedException();
        }
    }
}
