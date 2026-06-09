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
            return await _graphGroupInformationReader.GroupExistsAsync(objectId, ResolveRunId());
        }

        public async Task<bool> GroupExists(string groupName)
        {
            return await _graphGroupInformationReader.GroupExistsAsync(groupName, ResolveRunId());
        }

        public async Task<bool> IsGroupSyncedOnPremisesAsync(Guid groupId)
        {
            return await _graphGroupInformationReader.IsGroupSyncedOnPremisesAsync(groupId, ResolveRunId());
        }

        public async Task<AzureADGroup> GetGroup(string groupName)
        {
            return await _graphGroupInformationReader.GetGroupAsync(groupName, ResolveRunId());
        }

        public async Task<string> GetGroupNameAsync(Guid objectId)
        {
            return await _graphGroupInformationReader.GetGroupNameAsync(objectId, ResolveRunId());
        }

        public Task<Dictionary<Guid, string>> GetGroupNamesAsync(List<Guid> objectIds)
        {
            return _graphGroupInformationReader.GetGroupNamesAsync(objectIds);
        }

        public Task<string> GetGroupEmailAsync(Guid objectId)
        {
            return _graphGroupInformationReader.GetGroupEmailAsync(objectId, ResolveRunId());
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
            return _graphGroupInformationReader.GetGroupEndpointsAsync(groupId, ResolveRunId());
        }

        public Task<string?> GetGroupVivaEngageUrlAsync(Guid groupId)
        {
            return _graphGroupInformationReader.GetGroupVivaEngageUrlAsync(groupId, RunId);
        }


        public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid groupId)
        {
            return await _graphGroupMembershipReader.GetChildrenOfGroup(groupId, ResolveRunId());
        }

        public async Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId, bool validateGroupExists = true)
        {
            if (validateGroupExists)
            {
                var groupExists = await _graphGroupInformationReader.GroupExistsAsync(groupObjectId, ResolveRunId());
                if (!groupExists) return false;
            }

            return await _graphGroupOwnerReader.IsAppIDOwnerOfGroupAsync(appId, groupObjectId, ResolveRunId());
        }

        public async Task<bool> IsServiceAccountOwnerOfGroupAsync(Guid serviceAccountObjectId, Guid groupObjectId)
        {
            return await _graphGroupOwnerReader.IsServiceAccountOwnerOfGroupAsync(serviceAccountObjectId, groupObjectId, ResolveRunId());
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string userIdentifier, Guid groupObjectId, bool validateGroupExists = true)
        {
            if (validateGroupExists)
            {
                var groupExists = await _graphGroupInformationReader.GroupExistsAsync(groupObjectId, ResolveRunId());
                if (!groupExists) return false;
            }

            return await _graphGroupOwnerReader.IsEmailRecipientOwnerOfGroupAsync(userIdentifier, groupObjectId, ResolveRunId());
        }

        public async Task<bool> IsEmailRecipientMemberOfGroupAsync(string userIdentifier, Guid groupObjectId)
        {
            var groupExists = await _graphGroupInformationReader.GroupExistsAsync(groupObjectId, ResolveRunId());
            if (!groupExists) return false;

            return await _graphGroupMembershipReader.IsEmailRecipientMemberOfGroupAsync(userIdentifier, groupObjectId, ResolveRunId());
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupOwnerReader.GetGroupOwnersAsync(groupObjectId, ResolveRunId(), top);
        }

        public async Task<AzureADGroup> CreateGroup(string newGroupName, TestGroupType testGroupType)
        {
            return await _graphGroupInformationReader.CreateGroupAsync(newGroupName, testGroupType, ResolveRunId());
        }

        public async Task AddGroupOwners(string groupId, List<Guid> ownerIds)
        {
            await _graphGroupInformationReader.AddGroupOwnersAsync(groupId, ownerIds, ResolveRunId());
        }

        public async Task<List<Guid>> GetGroupIdsOwnedByServicePrincipalAsync(Guid servicePrincipalObjectId)
        {
            return await _graphGroupOwnerReader.GetGroupIdsOwnedByServicePrincipalAsync(servicePrincipalObjectId, ResolveRunId());
        }

        public async Task<AzureADGroup> CreateGroupFromUI(string groupName, Guid groupOwnerId, string groupAlias)
        {
            return await _graphGroupInformationReader.CreateGroupFromUIAsync(groupName, groupOwnerId, groupAlias, ResolveRunId());
        }

        public async Task<List<AzureADUser>> GetTenantUsers(int userCount)
        {
            return await _graphUserReader.GetTenantUsersAsync(userCount, ResolveRunId());
        }

        public async Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid groupId)
        {
            return await _graphGroupMembershipReader.GetUsersInGroupTransitivelyAsync(groupId, ResolveRunId());
        }

        public async Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)>
            AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            _graphGroupMembershipUpdater.RunId = ResolveRunId() ?? Guid.Empty;
            return await _graphGroupMembershipUpdater.AddUsersToGroup(users, targetGroup);
        }

        public async Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)>
            RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            _graphGroupMembershipUpdater.RunId = ResolveRunId() ?? Guid.Empty;
            return await _graphGroupMembershipUpdater.RemoveUsersFromGroup(users, targetGroup);
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetFirstTransitiveMembersPageAsync(Guid groupId)
        {
            return await _graphGroupMembershipReader.GetFirstTransitiveMembersPageAsync(groupId, ResolveRunId());
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetNextTransitiveMembersPageAsync(Guid groupId, string nextPageUrl)
        {
            return await _graphGroupMembershipReader.GetNextTransitiveMembersPageAsync(groupId, nextPageUrl, ResolveRunId());
        }

        public async Task<AzureADUser> GetUserByUpnOrIdAsync(string userIdentifier, bool includeMailProperty = false)
        {
            return await _graphUserReader.GetUserByUpnOrIdAsync(userIdentifier, ResolveRunId(), includeMailProperty);
        }

        public async Task<AzureADUser> GetUserWithOnPremisesImmutableIdAsync(string userIdentifier, Guid? runId)
        {
            return await _graphUserReader.GetUserWithOnPremisesImmutableIdAsync(userIdentifier, ResolveRunId(runId));
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetFirstMembersPageAsync(string url)
        {
            return await _graphUserReader.GetFirstMembersPageAsync(url, ResolveRunId());
        }

        public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)>
            GetNextMembersPageAsync(string nextPageUrl)
        {
            return await _graphUserReader.GetNextMembersPageAsync(nextPageUrl, ResolveRunId());
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip)
        {
            return await _graphGroupPlacesReader.GetRoomsPageAsync(url, top, skip, ResolveRunId());
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl)> GetWorkSpacesPageAsync(string url, int top, int skip)
        {
            return await _graphGroupPlacesReader.GetWorkSpacesPageAsync(url, top, skip, ResolveRunId());
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)>
            GetFirstDeltaLinkUsersPageAsync(Guid groupId, string deltaLink, int numberOfPages)
        {
            var (usersToAdd, usersToRemove, nextPageUrl, deltaUrl) = await _graphGroupDeltaReader.GetFirstDeltaLinkUsersPageAsync(groupId, deltaLink, ResolveRunId(), numberOfPages);

            _graphGroupRepositoryLogger.LogInformationWithRunId(ResolveRunId(), $"Number of users from first page using delta link - {usersToAdd.Count + usersToRemove.Count}");

            return (usersToAdd, usersToRemove, nextPageUrl, deltaUrl);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)>
            GetNextDeltaLinkUsersPagesAsync(Guid groupId, string nextPageUrl, int numberOfPages)
        {
            var (usersToAdd, usersToRemove, newNextPageUrl, deltaUrl) = await _graphGroupDeltaReader.GetNextDeltaLinkUsersPagesAsync(groupId, nextPageUrl, ResolveRunId(), numberOfPages);

            _graphGroupRepositoryLogger.LogInformationWithRunId(ResolveRunId(), $"Number of users from next page using delta link - {usersToAdd.Count + usersToRemove.Count}");

            return (usersToAdd, usersToRemove, newNextPageUrl, deltaUrl);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetFirstDeltaUsersPageAsync(Guid groupId, int numberOfPages)
        {
            return await _graphGroupDeltaReader.GetFirstDeltaUsersPageAsync(groupId, ResolveRunId(), numberOfPages);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetNextDeltaUsersPagesAsync(Guid groupId, string nextPageUrl, int numberOfPages)
        {
            return await _graphGroupDeltaReader.GetNextDeltaUsersPagesAsync(groupId, nextPageUrl, ResolveRunId(), numberOfPages);
        }

        public async Task<int> GetGroupsCountAsync(Guid objectId)
        {
            return await _graphGroupMembershipReader.GetGroupsCountAsync(objectId, ResolveRunId());
        }

        public async Task<int> GetUsersCountAsync(Guid objectId)
        {
            return await _graphGroupMembershipReader.GetUsersCountAsync(objectId, ResolveRunId());
        }

        public async Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds)
        {
            return await _graphGroupInformationReader.GetGroupsAsync(groupIds, ResolveRunId());
        }

        public async Task<List<AzureADGroup>> SearchDestinationsAsync(string query)
        {
            return await _graphGroupInformationReader.SearchGroupsAsync(query);
        }
        public async Task<Dictionary<Guid, string>> GetAllGroupNamesAsync()
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
            var children = await _graphGroupMembershipReader.GetDirectGroupMembersAsync(groupObjectId, ResolveRunId());
            return children.OfType<AzureADGroup>().ToList();
        }

        private Guid? ResolveRunId(Guid? runId = null)
        {
            return CorrelationActivity.ResolveRunId(runId, RunId == Guid.Empty ? null : RunId);
        }
    }
}
