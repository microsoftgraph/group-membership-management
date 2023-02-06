// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Models.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
	public interface IGraphGroupRepository
	{
		public Guid RunId { get; set; }

		// Only the circular reference checker uses this, it can be removed when we get rid of the circular reference checker.
		Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId);
		Task<string> GetGroupNameAsync(Guid objectId);
        Task<List<string>> GetGroupEndpointsAsync(Guid groupId);
        Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId);
		Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId);
		Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
		Task<bool> GroupExists(Guid objectId);
		Task<bool> GroupExists(string groupName);
		Task<AzureADGroup> GetGroup(string groupName);
		Task CreateGroup(string newGroupName);
		Task<List<AzureADUser>> GetTenantUsers(int userCount);
		Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId);
		Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
		Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
		Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersPageByIdAsync(string groupId);
		Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersNextPageAsnyc(IGroupTransitiveMembersCollectionWithReferencesPage groupMembersRef, string nextPageUrl);
		Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstTransitiveMembersPageAsync(Guid objectId);
		Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextTransitiveMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup);
		Task<User> GetUserByEmail(string emailAddress);
        Task<IGraphServiceUsersCollectionPage> GetFirstMembersAsync(string url);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGraphServiceUsersCollectionPage usersFromGroup)> GetFirstMembersPageAsync(string url);
        Task<IGraphServiceUsersCollectionPage> GetNextMembersAsync(IGraphServiceUsersCollectionPage groupMembersRef, string nextPageUrl);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGraphServiceUsersCollectionPage usersFromGroup)> GetNextMembersPageAsync(string nextPageUrl, IGraphServiceUsersCollectionPage usersFromGroup);
		Task<IGraphServicePlacesCollectionPage> GetRoomsAsync(string url, int top, int skip);
		Task<(List<AzureADUser> users, IGraphServicePlacesCollectionPage usersFromGroup)> GetRoomsPageAsync(string url, int top, int skip);
        Task<IGraphServicePlacesCollectionPage> GetWorkSpacesAsync(string url, int top, int skip);
        Task<(List<AzureADUser> users, IGraphServicePlacesCollectionPage usersFromGroup)> GetWorkSpacesPageAsync(string url, int top, int skip);
        Task<IGroupDeltaCollectionPage> GetGroupUsersPageByIdAsync(string groupId);
		Task<IGroupDeltaCollectionPage> GetGroupUsersNextPageAsnyc(IGroupDeltaCollectionPage groupMembersRef, string nextPageUrl);
		Task<IGroupDeltaCollectionPage> GetGroupUsersPageByLinkAsync(string deltaLink);
		Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetFirstDeltaUsersPageAsync(string deltaLink);
		Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetNextDeltaUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage usersFromGroup);
		Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetFirstUsersPageAsync(Guid objectId);
		Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage usersFromGroup);
		Task<int> GetGroupsCountAsync(Guid objectId);
		Task<int> GetUsersCountAsync(Guid objectId);
    }
}