// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;

namespace Services.TeamsChannelUpdater.Contracts
{
    public interface ITeamsChannelUpdaterService
    {
        public Guid RunId { get; set; }
        Task<SyncJob> GetSyncJobAsync(Guid syncJobId);
        Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId);
        public Task MarkSyncJobAsErroredAsync(SyncJob syncJob);
        public Task<(int SuccessCount, List<AzureADTeamsUser> UsersToRetry)> AddUsersToChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members);
        public Task<(int SuccessCount, List<AzureADTeamsUser> UserRemovesFailed)> RemoveUsersFromChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members);
        public Task<string> GetGroupNameAsync(Guid groupId, Guid runId);
        public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid runId, int top = 0);
        public Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, string ccEmail = null, string emailSubject = null, string[] additionalSubjectParams = null, string adaptiveCardTemplateDirectory = "");
    }
}
