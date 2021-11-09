// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Polly;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
	public interface IGraphUpdaterService
	{
		public Task<UsersPageResponse> GetFirstMembersPageAsync(Guid groupId, Guid runId);
		public Task<UsersPageResponse> GetNextMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage membersPage, Guid runId);
		public Task<PolicyResult<bool>> GroupExistsAsync(Guid groupId, Guid runId);
		public Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, string ccEmail = null, string emailSubject = null, string[] additionalSubjectParams = null);
		public Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey);
		public Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId);
		public Task<string> GetGroupNameAsync(Guid groupId);
		public Task<(GraphUpdaterStatus Status, int SuccessCount)> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync);
		public Task<(GraphUpdaterStatus Status, int SuccessCount)> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync);
		public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId);
		public Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
	}
}