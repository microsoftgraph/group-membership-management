// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Polly;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IGraphUpdaterService
	{
		public Guid RunId { get; set; }
		public Task<UsersPageResponse> GetFirstMembersPageAsync(Guid groupId, Guid runId);
		public Task<UsersPageResponse> GetNextMembersPageAsync(string nextPageUrl, Guid runId);
		public Task<bool> GroupExistsAsync(Guid groupId, Guid runId);
		public Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, Guid jobId, string ccEmail = null, string emailSubject = null, string[] additionalSubjectParams = null, string adaptiveCardTemplateDirectory = "");
		public Task<SyncJob> GetSyncJobAsync(Guid syncJobId);
		public Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId);
		public Task<string> GetGroupNameAsync(Guid groupId);
		public Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync);
		public Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync);
		public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId);
		public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
	}
}