// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using Models.Notifications;

namespace Services.TeamsChannelUpdater.Contracts
{
    public interface ITeamsChannelUpdaterService
    {
        public Guid RunId { get; set; }
        Task<SyncJob> GetSyncJobAsync(Guid syncJobId);
        Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId);
        public Task MarkSyncJobAsErroredAsync(SyncJob syncJob);
        public Task<(int SuccessCount, List<AzureADTeamsUser> UsersToRetry, List<AzureADTeamsUser> UsersNotFound)> AddUsersToChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members);
        public Task<(int SuccessCount, List<AzureADTeamsUser> UserRemovesFailed)> RemoveUsersFromChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members);
        public Task<string> GetGroupNameAsync(Guid groupId, Guid runId);
        public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid runId, int top = 0);
        public Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParams);
    }
}
