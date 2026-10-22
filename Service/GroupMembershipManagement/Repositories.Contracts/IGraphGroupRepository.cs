// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Entities;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
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
        Task<(string Email, DateTimeOffset? CreatedDateTime)> GetGroupEmailAndCreatedDateAsync(Guid objectId);
        Task<Dictionary<Guid, string>> GetGroupEmailsAsync(List<Guid> objectIds);

        Task<List<string>> GetGroupEndpointsAsync(Guid groupId);
        Task<string?> GetGroupVivaEngageUrlAsync(Guid groupId);
        Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId, bool validateGroupExists = true);
        Task<bool> IsServiceAccountOwnerOfGroupAsync(Guid serviceAccountObjectId, Guid groupObjectId);
        Task<bool> IsEmailRecipientOwnerOfGroupAsync(string userIdentifier, Guid groupObjectId, bool validateGroupExists = true);
        Task<bool> IsEmailRecipientMemberOfGroupAsync(string userIdentifier, Guid groupObjectId);
        /// <summary>
        /// Reads whether a directory user account is enabled and the Graph type annotation needed to distinguish
        /// an agent user from a person.
        /// </summary>
        /// <param name="userIdentifier">The user's object identifier or user principal name.</param>
        /// <returns>
        /// The account-enabled value and raw Graph OData type. When the user cannot be read, both values are
        /// <see langword="null"/>. A person is returned as <c>#microsoft.graph.user</c>; an agent user is
        /// returned as <c>#microsoft.graph.agentUser</c>.
        /// </returns>
        Task<(bool? AccountEnabled, string? ODataType)> GetUserAccountEnabledAsync(string userIdentifier);

        /// <summary>Returns the assistants published on a leader's directory profile.</summary>
        /// <remarks>Empty means no assistant; an unreadable profile throws so callers can fail closed.</remarks>
        Task<List<AzureADUser>> GetAssistantsAsync(Guid leaderObjectId);
        Task<Dictionary<Guid, List<Guid>>> GetDestinationOwnersAsync(List<Guid> objectIds);
        Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
        Task<bool> GroupExists(Guid objectId);
        Task<bool> GroupExists(string groupName);
        Task<bool> IsGroupSyncedOnPremisesAsync(Guid groupId);
        Task<AzureADGroup> GetGroup(string groupName);
        Task<AzureADGroup> CreateGroup(string newGroupName, TestGroupType testGroupType);
        Task AddGroupOwners(string groupId, List<Guid> ownerIds);
        Task<List<Guid>> GetGroupIdsOwnedByServicePrincipalAsync(Guid servicePrincipalObjectId);
        Task<AzureADGroup> CreateGroupFromUI(string newGroupName, Guid groupOwnerId, string newGroupAlias, NewGroupType groupType = NewGroupType.Microsoft365, string? description = null);
        Task<List<AzureADUser>> GetTenantUsers(int userCount);
        Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId);
        Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
        Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
        /// <summary>
        /// Reads the first bounded set of transitive-membership pages for a group.
        /// </summary>
        /// <param name="objectId">The source group's object identifier.</param>
        /// <param name="identityTypes">
        /// The explicit group-part selection. Pass <see langword="null"/> or both <c>users</c> and <c>agents</c>
        /// for the existing uncast read, only <c>users</c> for people, or only <c>agents</c> for agent users.
        /// </param>
        /// <returns>
        /// Members, counts for objects Graph returned, and the next-link URL. The next link is null when all pages
        /// were read.
        /// </returns>
        Task<(List<AzureADUser> users, MemberReadCounts counts, string nextPageUrl)> GetFirstTransitiveMembersPageAsync(
            Guid objectId,
            IReadOnlyList<string>? identityTypes = null);

        /// <summary>
        /// Continues a transitive-membership read) from a Graph next link.
        /// </summary>
        /// <param name="objectId">The source group's object identifier, used for telemetry.</param>
        /// <param name="nextPageUrl">The nonempty next-link URL returned by the preceding Graph response.</param>
        /// <param name="identityTypes">
        /// The same selection used for the first page. Pass <see langword="null"/> or both supported values for
        /// the uncast read, only <c>users</c> for people, or only <c>agents</c> for agent users.
        /// </param>
        /// <returns>
        /// Members, counts for objects Graph returned, and the next-link URL. The next link is null when all pages
        /// were read.
        /// </returns>
        Task<(List<AzureADUser> users, MemberReadCounts counts, string nextPageUrl)> GetNextTransitiveMembersPageAsync(
            Guid objectId,
            string nextPageUrl,
            IReadOnlyList<string>? identityTypes = null);

        Task<AzureADUser> GetUserByUpnOrIdAsync(string userIdentifier, bool includeMailProperty);
        Task<AzureADUser> GetUserWithOnPremisesImmutableIdAsync(string userIdentifier, Guid? runId);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetFirstMembersPageAsync(string url);
        Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl)> GetNextMembersPageAsync(string nextPageUrl);
        Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip);
        Task<(List<AzureADUser> users, string nextPageUrl)> GetWorkSpacesPageAsync(string url, int top, int skip);
        Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, MemberReadCounts counts, string nextPageUrl, string deltaUrl)> GetFirstDeltaLinkUsersPageAsync(Guid objectId, string deltaLink, int numberOfPages);
        Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, MemberReadCounts counts, string nextPageUrl, string deltaUrl)> GetNextDeltaLinkUsersPagesAsync(Guid objectId, string nextPageUrl, int numberOfPages);
        Task<(List<AzureADUser> users, MemberReadCounts counts, string nextPageUrl, string deltaUrl)> GetFirstDeltaUsersPageAsync(Guid objectId, int numberOfPages);
        Task<(List<AzureADUser> users, MemberReadCounts counts, string nextPageUrl, string deltaUrl)> GetNextDeltaUsersPagesAsync(Guid objectId, string nextPageUrl, int numberOfPages);

        /// <summary>
        /// Identifies the user-family type of directory objects by identifier.
        /// </summary>
        /// <param name="ids">
        /// Up to 1,000 identifiers. An empty collection returns an empty result without calling Graph; null and
        /// collections larger than 1,000 are rejected.
        /// </param>
        /// <param name="runId">The optional run identifier used to correlate retry logging.</param>
        /// <param name="cancellationToken">
        /// Cancels the Graph request and any waits performed by the shared retry policies.
        /// </param>
        /// <returns>
        /// A map containing each object Graph returned. Values are normalized to <c>user</c> or
        /// <c>agentUser</c>. Identifiers omitted by a successful Graph response remain absent.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="ids"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ids"/> contains more than 1,000 values.</exception>
        /// <exception cref="HttpRequestException">
        /// Graph still returns a non-success status after the shared retry policies' configured attempts, or the
        /// transport fails after them. <see cref="HttpRequestException.StatusCode"/> is the last status, or null for a
        /// transport failure.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Graph returns an unreadable response, a returned object has no type, or a returned type is unsupported.
        /// </exception>
        Task<IReadOnlyDictionary<Guid, string>> GetDirectoryObjectTypesAsync(
            IReadOnlyCollection<Guid> ids,
            Guid? runId,
            CancellationToken cancellationToken);
        Task<int> GetGroupsCountAsync(Guid objectId);
        Task<int> GetUsersCountAsync(Guid objectId);

        /// <summary>
        /// Counts the Entra agentUser directory objects reachable transitively from the group.
        /// Telemetry only - never used to determine membership. Returns <see langword="null"/> when the count cannot
        /// be read (an exception, no response, a non-success status, or a missing or invalid <c>@odata.count</c>);
        /// a failed count is never reported as zero (FR-020).
        /// </summary>
        Task<int?> GetAgentUserCountAsync(Guid objectId);
        Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds);
        Task<List<AzureADGroup>> SearchDestinationsAsync(string query);
        Task<List<AzureADGroup>> SearchDestinationsBySearchAsync(string search);
        Task<List<AzureADGroup>> GetGroupsByFilterAsync(string query);
        Task<Dictionary<Guid, string>> GetAllGroupNamesAsync();
        Task<Guid> GetObjectIdFromAppIdAsync(Guid userIdentifier, Guid? runId);
        Task<List<AzureADGroup>> GetDirectGroupTypeMembersAsync(Guid groupObjectId);
    }
}