// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
	public interface IGraphGroupRepository
	{
		public Guid RunId { get; set; }

		// Only the circular reference checker uses this, it can be removed when we get rid of the circular reference checker.
		Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId);
		Task<string> GetGroupNameAsync(Guid objectId);
		Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId);
		Task<bool> GroupExists(Guid objectId);
		Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId);
		Task<ResponseCode> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
		Task<ResponseCode> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup);
		Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersPageByIdAsync(string groupId);
		Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersNextPageAsnyc(IGroupTransitiveMembersCollectionWithReferencesPage groupMembersRef, string nextPageUrl);
		Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstUsersPageAsync(Guid objectId);
		Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup);
	}
}