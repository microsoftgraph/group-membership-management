// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Notifications;
using Models.SyncJobChange;

namespace Services.WebApi.Contracts
{
    public interface INotificationService
    {
        Task SendSubmissionRejectedNotificationAsync(
            SyncJob syncJob, 
            SyncJobChange submission);

        Task SendSubmissionApprovedNotificationAsync(
            SyncJob syncJob, 
            SyncJobChange submission);

        Task SendReviewStatusChangeNotificationAsync(
            SyncJob syncJob,
            SyncJobChange submission,
            NotificationMessageType notificationType);

        Task SendNotificationAsync(
            SyncJob syncJob,
            NotificationMessageType notificationType,
            Dictionary<string, object>? customProperties = null);
    }
}
