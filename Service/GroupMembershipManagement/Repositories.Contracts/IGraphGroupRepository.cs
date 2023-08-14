// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
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
        Task<Dictionary<Guid, string>> GetGroupNamesAsync(List<Guid> objectIds);
        Task<List<string>> GetGroupEndpointsAsync(Guid groupId);
        Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId);
        Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId);
        Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
        Task<bool> GroupExists(Guid objectId);
        Task<bool> GroupExists(string groupName);
        Task<AzureADGroup> GetGroup(string groupName);
        Task CreateGroup(string newGroupName);
        Task<List<AzureADUser>> GetTenantUsers(int userCount);
        Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId);
        Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
        Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid objectId);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextTransitiveMembersPageAsync(string nextPageUrl);
        Task<AzureADUser> GetUserByEmailAsync(string emailAddress);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstMembersPageAsync(string url);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextMembersPageAsync(string nextPageUrl);
        Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip);
        Task<(List<AzureADUser> users, string nextPageUrl)> GetWorkSpacesPageAsync(string url, int top, int skip);
        Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)> GetFirstDeltaUsersPageAsync(string deltaLink);
        Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)> GetNextDeltaUsersPageAsync(string nextPageUrl);
        Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetFirstUsersPageAsync(Guid objectId);
        Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetNextUsersPageAsync(string nextPageUrl);
        Task<int> GetGroupsCountAsync(Guid objectId);
        Task<int> GetUsersCountAsync(Guid objectId);
        Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds);
    }
}