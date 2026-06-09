// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
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
        Task<Dictionary<Guid, string>> GetGroupNamesAsync(List<Guid> objectIds);
        Task<string> GetGroupEmailAsync(Guid objectId);
        Task<Dictionary<Guid, string>> GetGroupEmailsAsync(List<Guid> objectIds);

        Task<List<string>> GetGroupEndpointsAsync(Guid groupId);
        Task<string?> GetGroupVivaEngageUrlAsync(Guid groupId);
        Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId, bool validateGroupExists = true);
        Task<bool> IsServiceAccountOwnerOfGroupAsync(Guid serviceAccountObjectId, Guid groupObjectId);
        Task<bool> IsEmailRecipientOwnerOfGroupAsync(string userIdentifier, Guid groupObjectId, bool validateGroupExists = true);
        Task<bool> IsEmailRecipientMemberOfGroupAsync(string userIdentifier, Guid groupObjectId);
        Task<Dictionary<Guid, List<Guid>>> GetDestinationOwnersAsync(List<Guid> objectIds);
        Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
        Task<bool> GroupExists(Guid objectId);
        Task<bool> GroupExists(string groupName);
        Task<bool> IsGroupSyncedOnPremisesAsync(Guid groupId);
        Task<AzureADGroup> GetGroup(string groupName);
        Task<AzureADGroup> CreateGroup(string newGroupName, TestGroupType testGroupType);
        Task AddGroupOwners(string groupId, List<Guid> ownerIds);
        Task<List<Guid>> GetGroupIdsOwnedByServicePrincipalAsync(Guid servicePrincipalObjectId);
        Task<AzureADGroup> CreateGroupFromUI(string newGroupName, Guid groupOwnerId, string newGroupAlias);
        Task<List<AzureADUser>> GetTenantUsers(int userCount);
        Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId);
        Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
        Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid objectId);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextTransitiveMembersPageAsync(Guid objectId, string nextPageUrl);
        Task<AzureADUser> GetUserByUpnOrIdAsync(string userIdentifier, bool includeMailProperty);
        Task<AzureADUser> GetUserWithOnPremisesImmutableIdAsync(string userIdentifier, Guid? runId);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstMembersPageAsync(string url);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextMembersPageAsync(string nextPageUrl);
        Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip);
        Task<(List<AzureADUser> users, string nextPageUrl)> GetWorkSpacesPageAsync(string url, int top, int skip);
        Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)> GetFirstDeltaLinkUsersPageAsync(Guid objectId, string deltaLink, int numberOfPages);
        Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)> GetNextDeltaLinkUsersPagesAsync(Guid objectId, string nextPageUrl, int numberOfPages);
        Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetFirstDeltaUsersPageAsync(Guid objectId, int numberOfPages);
        Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetNextDeltaUsersPagesAsync(Guid objectId, string nextPageUrl, int numberOfPages);
        Task<int> GetGroupsCountAsync(Guid objectId);
        Task<int> GetUsersCountAsync(Guid objectId);
        Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds);
        Task<List<AzureADGroup>> SearchDestinationsAsync(string query);
        Task<List<AzureADGroup>> GetGroupsByFilterAsync(string query);
        Task<Dictionary<Guid, string>> GetAllGroupNamesAsync();
        Task<Guid> GetObjectIdFromAppIdAsync(Guid userIdentifier, Guid? runId);
        Task<List<AzureADGroup>> GetDirectGroupTypeMembersAsync(Guid groupObjectId);
    }
}