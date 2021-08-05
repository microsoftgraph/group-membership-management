// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Polly;
using Services.Entities;
using System;
using System.Threading.Tasks;

namespace Services.Contracts
{
	public interface IGraphUpdaterService
	{
		public Task<UsersPageResponse> GetFirstMembersPageAsync(Guid groupId, Guid runId);
		public Task<UsersPageResponse> GetNextMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage membersPage);
		public Task<PolicyResult<bool>> GroupExistsAsync(Guid groupId, Guid runId);
		public Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, string ccEmail = null);
		public Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey);
		public Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId);
		public Task<string> GetGroupNameAsync(Guid groupId);
	}
}