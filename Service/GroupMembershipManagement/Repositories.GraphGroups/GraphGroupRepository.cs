// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Models;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using static Microsoft.Azure.Amqp.Serialization.SerializableType;
using Group = Microsoft.Graph.Models.Group;
using Metric = Services.Entities.Metric;

namespace Repositories.GraphGroups
{
    public class GraphGroupRepository : IGraphGroupRepository
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILoggingRepository _loggingRepository;
        private readonly GraphGroupInformationReader _graphGroupInformationReader;
        private readonly GraphGroupOwnerReader _graphGroupOwnerReader;

        public Guid RunId { get; set; }

        public GraphGroupRepository(GraphServiceClient graphServiceClient,
                                    TelemetryClient telemetryClient,
                                    ILoggingRepository loggingRepository)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _ = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));

            var graphGroupMetricTracker = new GraphGroupMetricTracker(graphServiceClient, telemetryClient, loggingRepository);
            _graphGroupInformationReader = new GraphGroupInformationReader(graphServiceClient, loggingRepository, graphGroupMetricTracker);
            _graphGroupOwnerReader = new GraphGroupOwnerReader(graphServiceClient, telemetryClient, loggingRepository);
        }

        public async Task<bool> GroupExists(Guid objectId)
        {
            return await _graphGroupInformationReader.GroupExistsAsync(objectId, RunId);
        }

        public async Task<bool> GroupExists(string groupName)
        {
            return await _graphGroupInformationReader.GroupExistsAsync(groupName, RunId);
        }

        public async Task<AzureADGroup> GetGroup(string groupName)
        {
            return await _graphGroupInformationReader.GetGroupAsync(groupName, RunId);
        }

        public async Task<string> GetGroupNameAsync(Guid objectId)
        {
            return await _graphGroupInformationReader.GetGroupNameAsync(objectId, RunId);
        }

        public Task<List<string>> GetGroupEndpointsAsync(Guid groupId)
        {
            return _graphGroupInformationReader.GetGroupEndpointsAsync(groupId, RunId);
        }

        public Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
        {
            var groupExists = await _graphGroupInformationReader.GroupExistsAsync(groupObjectId, RunId);
            if (!groupExists) return false;

            return await _graphGroupOwnerReader.IsAppIDOwnerOfGroupAsync(appId, groupObjectId, RunId);
        }

        public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            throw new NotImplementedException();
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupOwnerReader.GetGroupOwnersAsync(groupObjectId, RunId, top);
        }

        public Task CreateGroup(string newGroupName)
        {
            throw new NotImplementedException();
        }

        public Task<List<AzureADUser>> GetTenantUsers(int userCount)
        {
            throw new NotImplementedException();
        }

        public Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            throw new NotImplementedException();
        }

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            throw new NotImplementedException();
        }

        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextTransitiveMembersPageAsync(string nextPageUrl)
        {
            throw new NotImplementedException();
        }

        public Task<AzureADUser> GetUserByEmailAsync(string emailAddress)
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

        public Task<int> GetGroupsCountAsync(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public Task<int> GetUsersCountAsync(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds)
        {
            throw new NotImplementedException();
        }
    }
}