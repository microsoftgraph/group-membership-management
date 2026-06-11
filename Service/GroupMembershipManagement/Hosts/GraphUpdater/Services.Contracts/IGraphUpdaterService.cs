// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IGraphUpdaterService
	{
		public Guid RunId { get; set; }
		public Task<bool> GroupExistsAsync(Guid groupId, Guid runId);
		public Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters);
		public Task<SyncJob> GetSyncJobAsync(Guid syncJobId);
		public Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId, int? usersAdded, int? usersRemoved);
		public Task<string> GetGroupNameAsync(Guid groupId);
		public Task<Guid> GetGroupIdAsync(SyncJob syncJob);
        public Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync);
		public Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync);
		public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId);
		public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
	}
}