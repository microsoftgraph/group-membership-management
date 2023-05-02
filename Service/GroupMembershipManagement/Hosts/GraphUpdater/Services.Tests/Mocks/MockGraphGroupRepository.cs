// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests.Mocks
{
    class MockGraphGroupRepository : IGraphGroupRepository
    {
        public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; } = new Dictionary<Guid, List<AzureADUser>>();
        public Guid RunId { get; set; }

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            GroupsToUsers[targetGroup.ObjectId].AddRange(users);
            return Task.FromResult((ResponseCode.Ok, users.ToList().Count, new List<AzureADUser>(), new List<AzureADUser>()));
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

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            GroupsToUsers[targetGroup.ObjectId].RemoveAll(x => users.Contains(x));
            return Task.FromResult((ResponseCode.Ok, users.ToList().Count, new List<AzureADUser>(), new List<AzureADUser>()));
        }
        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextTransitiveMembersPageAsync(string nextPageUrl)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            throw new NotImplementedException();
        }

        public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
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
        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstMembersPageAsync(string url)
        {
            throw new NotImplementedException();
        }
        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextMembersPageAsync(string nextPageUrl)
        {
            throw new NotImplementedException();
        }
        public Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip)
        {
            throw new NotImplementedException();
        }
        public Task<(List<AzureADUser> users, string nextPageUrl)> GetWorkSpacesPageAsync(string url, int top, int skip)
        {
            throw new NotImplementedException();
        }
        public Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)> GetFirstDeltaUsersPageAsync(string deltaLink)
        {
            throw new NotImplementedException();
        }
        public Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)> GetNextDeltaUsersPageAsync(string nextPageUrl)
        {
            throw new NotImplementedException();
        }
        public Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetFirstUsersPageAsync(Guid objectId)
        {
            throw new NotImplementedException();
        }
        public Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetNextUsersPageAsync(string nextPageUrl)
        {
            throw new NotImplementedException();
        }
        public Task<AzureADUser> GetUserByEmailAsync(string emailAddress)
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
