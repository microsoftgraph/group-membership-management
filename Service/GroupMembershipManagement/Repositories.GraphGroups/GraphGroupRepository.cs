// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Models;
using Models.Entities;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    public class GraphGroupRepository : IGraphGroupRepository
    {
        private readonly ILogger<GraphGroupRepository> _graphGroupRepositoryLogger;
        private readonly GraphGroupInformationRepository _graphGroupInformationReader;
        private readonly GraphGroupOwnerReader _graphGroupOwnerReader;
        private readonly GraphUserReader _graphUserReader;
        private readonly GraphGroupMembershipReader _graphGroupMembershipReader;
        private readonly GraphGroupMembershipUpdater _graphGroupMembershipUpdater;
        private readonly GraphGroupDeltaReader _graphGroupDeltaReader;
        private readonly GraphGroupPlacesReader _graphGroupPlacesReader;

        public Guid RunId { get; set; }

        public GraphGroupRepository(GraphServiceClient graphServiceClient,
                                    TelemetryClient telemetryClient,
                                    ILoggerFactory loggerFactory = null)
            : this(graphServiceClient, telemetryClient, null, loggerFactory)
        {
        }

        public GraphGroupRepository(GraphServiceClient graphServiceClient,
                                    TelemetryClient telemetryClient,
                                    IGraphRepositorySettings graphRepositorySettings,
                                    ILoggerFactory loggerFactory = null)
        {
            if (graphServiceClient == null) throw new ArgumentNullException(nameof(graphServiceClient));
            _ = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphGroupRepositoryLogger = loggerFactory?.CreateLogger<GraphGroupRepository>() ?? NullLogger<GraphGroupRepository>.Instance;

            var graphGroupMetricTracker = new GraphGroupMetricTracker(
                graphServiceClient,
                telemetryClient,
                loggerFactory?.CreateLogger<GraphGroupMetricTracker>() ?? NullLogger<GraphGroupMetricTracker>.Instance);
            _graphGroupInformationReader = new GraphGroupInformationRepository(
                graphServiceClient,
                graphGroupMetricTracker,
                loggerFactory?.CreateLogger<GraphGroupInformationRepository>() ?? NullLogger<GraphGroupInformationRepository>.Instance);
            _graphGroupOwnerReader = new GraphGroupOwnerReader(
                graphServiceClient,
                graphGroupMetricTracker,
                loggerFactory?.CreateLogger<GraphGroupOwnerReader>() ?? NullLogger<GraphGroupOwnerReader>.Instance);
            _graphUserReader = new GraphUserReader(
                graphServiceClient,
                graphGroupMetricTracker,
                loggerFactory?.CreateLogger<GraphUserReader>() ?? NullLogger<GraphUserReader>.Instance);
            _graphGroupMembershipReader = new GraphGroupMembershipReader(
                graphServiceClient,
                graphGroupMetricTracker,
                loggerFactory?.CreateLogger<GraphGroupMembershipReader>() ?? NullLogger<GraphGroupMembershipReader>.Instance);
            _graphGroupMembershipUpdater = new GraphGroupMembershipUpdater(
                graphServiceClient,
                graphGroupMetricTracker,
                graphRepositorySettings,
                loggerFactory?.CreateLogger<GraphGroupMembershipUpdater>() ?? NullLogger<GraphGroupMembershipUpdater>.Instance);
            _graphGroupDeltaReader = new GraphGroupDeltaReader(
                graphServiceClient,
                graphGroupMetricTracker,
                loggerFactory?.CreateLogger<GraphGroupDeltaReader>() ?? NullLogger<GraphGroupDeltaReader>.Instance);
            _graphGroupPlacesReader = new GraphGroupPlacesReader(
                graphServiceClient,
                graphGroupMetricTracker,
                loggerFactory?.CreateLogger<GraphGroupPlacesReader>() ?? NullLogger<GraphGroupPlacesReader>.Instance,
                loggerFactory?.CreateLogger<GraphUserReader>() ?? NullLogger<GraphUserReader>.Instance);
        }

        public async Task<bool> GroupExists(Guid objectId)
        {
            return await _graphGroupInformationReader.GroupExistsAsync(objectId, RunId);
        }

        public async Task<bool> GroupExists(string groupName)
        {
            return await _graphGroupInformationReader.GroupExistsAsync(groupName, RunId);
        }

        public async Task<bool> IsGroupSyncedOnPremisesAsync(Guid groupId)
        {
            return await _graphGroupInformationReader.IsGroupSyncedOnPremisesAsync(groupId, RunId);
        }

        public async Task<AzureADGroup> GetGroup(string groupName)
        {
            return await _graphGroupInformationReader.GetGroupAsync(groupName, RunId);
        }

        public async Task<string> GetGroupNameAsync(Guid objectId)
        {
            return await _graphGroupInformationReader.GetGroupNameAsync(objectId, RunId);
        }

        public Task<Dictionary<Guid, string>> GetGroupNamesAsync(List<Guid> objectIds)
        {
            return _graphGroupInformationReader.GetGroupNamesAsync(objectIds);
        }

        public Task<string> GetGroupEmailAsync(Guid objectId)
        {
            return _graphGroupInformationReader.GetGroupEmailAsync(objectId, RunId);
        }

        public Task<Dictionary<Guid, string>> GetGroupEmailsAsync(List<Guid> objectIds)
        {
            return _graphGroupInformationReader.GetGroupEmailsAsync(objectIds);
        }

        public Task<Dictionary<Guid, List<Guid>>> GetDestinationOwnersAsync(List<Guid> objectIds)
        {
            return _graphGroupInformationReader.GetGroupOwnersAsync(objectIds);
        }

        public Task<List<string>> GetGroupEndpointsAsync(Guid groupId)
        {
            return _graphGroupInformationReader.GetGroupEndpointsAsync(groupId, RunId);
        }

        public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid groupId)
        {
            return await _graphGroupMembershipReader.GetChildrenOfGroup(groupId, RunId);
        }

        public async Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId, bool validateGroupExists = true)
        {
            if (validateGroupExists)
            {
                var groupExists = await _graphGroupInformationReader.GroupExistsAsync(groupObjectId, RunId);
                if (!groupExists) return false;
            }

            return await _graphGroupOwnerReader.IsAppIDOwnerOfGroupAsync(appId, groupObjectId, RunId);
        }

        public async Task<bool> IsServiceAccountOwnerOfGroupAsync(Guid serviceAccountObjectId, Guid groupObjectId)
        {
            return await _graphGroupOwnerReader.IsServiceAccountOwnerOfGroupAsync(serviceAccountObjectId, groupObjectId, RunId);
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string userIdentifier, Guid groupObjectId, bool validateGroupExists = true)
        {
            if (validateGroupExists)
            {
                var groupExists = await _graphGroupInformationReader.GroupExistsAsync(groupObjectId, RunId);
                if (!groupExists) return false;
            }

            return await _graphGroupOwnerReader.IsEmailRecipientOwnerOfGroupAsync(userIdentifier, groupObjectId, RunId);
        }

        public async Task<bool> IsEmailRecipientMemberOfGroupAsync(string userIdentifier, Guid groupObjectId)
        {
            var groupExists = await _graphGroupInformationReader.GroupExistsAsync(groupObjectId, RunId);
            if (!groupExists) return false;

            return await _graphGroupMembershipReader.IsEmailRecipientMemberOfGroupAsync(userIdentifier, groupObjectId, RunId);
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupOwnerReader.GetGroupOwnersAsync(groupObjectId, RunId, top);
        }

        public async Task CreateGroup(string newGroupName, TestGroupType testGroupType, List<Guid> groupOwnerIds)
        {
            await _graphGroupInformationReader.CreateGroupAsync(newGroupName, testGroupType, groupOwnerIds, RunId);
        }

        public async Task<AzureADGroup> CreateGroupFromUI(string groupName, Guid groupOwnerId, string groupAlias)
        {
            return await _graphGroupInformationReader.CreateGroupFromUIAsync(groupName, groupOwnerId, groupAlias, RunId);
        }

        public async Task<List<AzureADUser>> GetTenantUsers(int userCount)
        {
            return await _graphUserReader.GetTenantUsersAsync(userCount, RunId);
        }

        public async Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid groupId)
        {
            return await _graphGroupMembershipReader.GetUsersInGroupTransitivelyAsync(groupId, RunId);
        }

        public async Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)>
            AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            _graphGroupMembershipUpdater.RunId = RunId;
            return await _graphGroupMembershipUpdater.AddUsersToGroup(users, targetGroup);
        }

        public async Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)>
            RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            _graphGroupMembershipUpdater.RunId = RunId;
            return await _graphGroupMembershipUpdater.RemoveUsersFromGroup(users, targetGroup);
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetFirstTransitiveMembersPageAsync(Guid groupId)
        {
            return await _graphGroupMembershipReader.GetFirstTransitiveMembersPageAsync(groupId, RunId);
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetNextTransitiveMembersPageAsync(Guid groupId, string nextPageUrl)
        {
            return await _graphGroupMembershipReader.GetNextTransitiveMembersPageAsync(groupId, nextPageUrl, RunId);
        }

        public async Task<AzureADUser> GetUserByUpnOrIdAsync(string userIdentifier, bool includeMailProperty = false)
        {
            return await _graphUserReader.GetUserByUpnOrIdAsync(userIdentifier, RunId, includeMailProperty);
        }

        public async Task<AzureADUser> GetUserWithOnPremisesImmutableIdAsync(string userIdentifier, Guid? runId)
        {
            return await _graphUserReader.GetUserWithOnPremisesImmutableIdAsync(userIdentifier, runId ?? RunId);
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetFirstMembersPageAsync(string url)
        {
            return await _graphUserReader.GetFirstMembersPageAsync(url, RunId);
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetNextMembersPageAsync(string nextPageUrl)
        {
            return await _graphUserReader.GetNextMembersPageAsync(nextPageUrl, RunId);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip)
        {
            return await _graphGroupPlacesReader.GetRoomsPageAsync(url, top, skip, RunId);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl)> GetWorkSpacesPageAsync(string url, int top, int skip)
        {
            return await _graphGroupPlacesReader.GetWorkSpacesPageAsync(url, top, skip, RunId);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)>
            GetFirstDeltaLinkUsersPageAsync(Guid groupId, string deltaLink, int numberOfPages)
        {
            var (usersToAdd, usersToRemove, nextPageUrl, deltaUrl) = await _graphGroupDeltaReader.GetFirstDeltaLinkUsersPageAsync(groupId, deltaLink, RunId, numberOfPages);

            _graphGroupRepositoryLogger.LogInformationWithRunId(RunId, $"Number of users from first page using delta link - {usersToAdd.Count + usersToRemove.Count}");

            return (usersToAdd, usersToRemove, nextPageUrl, deltaUrl);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)>
            GetNextDeltaLinkUsersPagesAsync(Guid groupId, string nextPageUrl, int numberOfPages)
        {
            var (usersToAdd, usersToRemove, newNextPageUrl, deltaUrl) = await _graphGroupDeltaReader.GetNextDeltaLinkUsersPagesAsync(groupId, nextPageUrl, RunId, numberOfPages);

            _graphGroupRepositoryLogger.LogInformationWithRunId(RunId, $"Number of users from next page using delta link - {usersToAdd.Count + usersToRemove.Count}");

            return (usersToAdd, usersToRemove, newNextPageUrl, deltaUrl);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetFirstDeltaUsersPageAsync(Guid groupId, int numberOfPages)
        {
            return await _graphGroupDeltaReader.GetFirstDeltaUsersPageAsync(groupId, RunId, numberOfPages);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetNextDeltaUsersPagesAsync(Guid groupId, string nextPageUrl, int numberOfPages)
        {
            return await _graphGroupDeltaReader.GetNextDeltaUsersPagesAsync(groupId, nextPageUrl, RunId, numberOfPages);
        }

        public async Task<int> GetGroupsCountAsync(Guid objectId)
        {
            return await _graphGroupMembershipReader.GetGroupsCountAsync(objectId, RunId);
        }

        public async Task<int> GetUsersCountAsync(Guid objectId)
        {
            return await _graphGroupMembershipReader.GetUsersCountAsync(objectId, RunId);
        }

        public async Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds)
        {
            return await _graphGroupInformationReader.GetGroupsAsync(groupIds, RunId);
        }

        public async Task<List<AzureADGroup>> SearchDestinationsAsync(string query)
        {
            return await _graphGroupInformationReader.SearchGroupsAsync(query);
        }
        public async Task<List<string>> GetAllGroupNamesAsync()
        {
            return await _graphGroupInformationReader.GetAllGroupNamesAsync();
        }

        public async Task<List<AzureADGroup>> GetGroupsByFilterAsync(string filter)
        {
            return await _graphGroupInformationReader.GetGroupsByFilterAsync(filter);
        }

        public async Task<Guid> GetObjectIdFromAppIdAsync(Guid appId, Guid? runId)
        {
            return await _graphUserReader.GetObjectIdFromServicePrincipalAsync(appId, runId);
        }

        public async Task<List<AzureADGroup>> GetDirectGroupTypeMembersAsync(Guid groupObjectId)
        {
            var children = await _graphGroupMembershipReader.GetDirectGroupMembersAsync(groupObjectId, RunId);
            return children.OfType<AzureADGroup>().ToList();
        }
    }
}
