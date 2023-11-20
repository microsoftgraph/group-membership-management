// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
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
        public int ThrowSocketExceptionsFromGroupExistsBeforeSuccess { get; set; } = 0;
        public bool ThrowNonSocketExceptionFromGroupExists { get; set; } = false;
        public int ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess { get; set; } = 0;
        public bool ThrowNonSocketExceptionFromGetUsersInGroup { get; set; } = false;
        public Guid RunId { get; set; }

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
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

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
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

        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid objectId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            return Task.FromResult((users, nonUserGraphObjects, ""));

        }

        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextTransitiveMembersPageAsync(string nextPageUrl)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            return Task.FromResult((users, nonUserGraphObjects, ""));
        }

        public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsEmailRecipientMemberOfGroupAsync(string email, Guid groupObjectId)
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

        public Task CreateGroup(string newGroupName, TestGroupType testGroupType, List<Guid> ownerIds)
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

        public Task<Dictionary<Guid, string>> GetGroupNamesAsync(List<Guid> objectIds)
        {
            throw new NotImplementedException();
        }
        
        public Task<List<AzureADGroup>> SearchDestinationsAsync(string query)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<Guid, List<Guid>>> GetDestinationOwnersAsync(List<Guid> objectIds)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<string, string>> GetTeamsChannelsNamesAsync(List<AzureADTeamsChannel> channels)
        {
            throw new NotImplementedException();
        }
    }

    public class MockException : Exception { }

}
