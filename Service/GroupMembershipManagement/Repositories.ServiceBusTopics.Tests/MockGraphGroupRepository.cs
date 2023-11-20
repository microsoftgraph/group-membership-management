// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Models.Entities;

namespace Repositories.ServiceBusTopics.Tests
{
    public class MockGraphGroupRepository : IGraphGroupRepository
    {
        public Guid RunId { get; set; }

        public HashSet<Guid> GroupsThatExist = new HashSet<Guid>();
        public HashSet<Guid> GroupsGMMOwns = new HashSet<Guid>();

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
            throw new NotImplementedException();
        }

        public Task<bool> GroupExists(Guid objectId)
        {
            return Task.FromResult(GroupsThatExist.Contains(objectId));
        }

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
        {
            return Task.FromResult(GroupsGMMOwns.Contains(groupObjectId));
        }

        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextTransitiveMembersPageAsync(string nextPageUrl)
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
            var owners = new List<AzureADUser>();
            for (var i = 0; i < 10; i++)
            {
                owners.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid(),
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
}
