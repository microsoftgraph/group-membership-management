// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;

namespace Services.Contracts
{
    public interface IAzureMaintenanceService
    {
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<List<PurgedSyncJob>> BackupInactiveJobsAsync(List<SyncJob> syncJobs);
        Task RemoveInactiveJobsAsync(IEnumerable<SyncJob> jobs);
        Task<int> RemoveBackupsAsync();
        Task ExpireNotificationsAsync(IEnumerable<SyncJob> jobs);
        Task<string> GetGroupNameAsync(Guid groupId);
        Task SendPurgingEmailAsync(PurgedSyncJob job, NotificationMessageType notificationType);
        Task SendWarningEmailAsync(SyncJob job, NotificationMessageType notificationType);
        Task<List<SyncJob>> GetJobsApproachingPurgingAsync();
        Task<int> PurgeOldHistoryAsync();
    }
}
