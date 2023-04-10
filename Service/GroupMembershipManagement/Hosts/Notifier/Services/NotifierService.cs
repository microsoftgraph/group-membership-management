// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using Repositories.Contracts.InjectConfig;
using Models.ThresholdNotifications;

namespace Services
{
    public class NotifierService : INotifierService
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IMailRepository _mailRepository = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly INotificationRepository _notificationRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public NotifierService(
            ILoggingRepository loggingRepository,
            IMailRepository mailRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            INotificationRepository notificationRepository,
            IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        public async Task SendEmailAsync(Guid targetOfficeGroupId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sending email to recipient addresses." });

            var owners = await _graphGroupRepository.GetGroupOwnersAsync(targetOfficeGroupId);
            var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));

            var message = new EmailMessage
            {
                Subject = "Test Subject",
                Content = "Test Content",
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = ownerEmails,
                CcEmailAddresses = _emailSenderAndRecipients.SupportEmailAddresses,
            };
            
            await _mailRepository.SendMailAsync(message, null);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sent email to recipient addresses." });
        }

        public async Task<List<Models.ThresholdNotifications.ThresholdNotification>> RetrieveQueuedNotifications()
        {
            var allNotifications = new List<Models.ThresholdNotifications.ThresholdNotification>();
            var notifications = _notificationRepository.GetQueuedNotificationsAsync();
            if (notifications == null) { return allNotifications; }
            await foreach (var notification in notifications)
            {
                allNotifications.Add(notification);
            }
            return allNotifications;
        }

        public async Task UpdateNotificationStatus(Models.ThresholdNotifications.ThresholdNotification notification, ThresholdNotificationStatus status)
        {
            await _notificationRepository.UpdateNotificationStatusAsync(notification, status);
        }

    }
}
