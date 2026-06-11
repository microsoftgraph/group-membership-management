// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Notifications;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDeferredNotificationsRepository
    {
        Task AddDeferredNotificationAsync(DeferredNotification deferredNotification);
        Task<IList<DeferredNotification>> GetDeferredNotificationsByTypeAsync(NotificationMessageType messageType);
        Task<IList<DeferredNotification>> GetDeferredNotificationsByTypeAndStatusAsync(NotificationMessageType messageType, DeferredNotificationStatus status);
        Task UpdateStatusAsync(int id, DeferredNotificationStatus status);
        Task UpdateStatusBatchAsync(IEnumerable<int> ids, DeferredNotificationStatus status);
        Task RemoveDeferredNotificationAsync(int id);
        Task RemoveDeferredNotificationsByTypeAndStatusAsync(NotificationMessageType messageType, DeferredNotificationStatus status);
    }
}
