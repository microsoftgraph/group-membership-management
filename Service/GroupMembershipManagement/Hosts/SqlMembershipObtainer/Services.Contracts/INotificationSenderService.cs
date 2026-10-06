// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface INotificationSenderService
    {
        /// <summary>
        /// Queues a disabled-sync notification for the given job. The positional
        /// <paramref name="additionalContentParameters"/> fill the notification's subject, title, and body templates.
        /// </summary>
        Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters);
    }
}
