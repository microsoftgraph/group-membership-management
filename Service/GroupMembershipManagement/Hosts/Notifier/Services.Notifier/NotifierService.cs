// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Notifications;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Services.Notifier.Contracts;
using System;
using System.Text.Json;
using System.Linq;
using Repositories.Contracts.InjectConfig;
using Models.ThresholdNotifications;
using Services.Contracts.Notifications;
using Microsoft.ApplicationInsights;
using Newtonsoft.Json;

namespace Services.Notifier
{
    public class NotifierService : INotifierService
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IMailRepository _mailRepository = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly ILocalizationRepository _localizationRepository = null;
        private readonly IThresholdNotificationService _thresholdNotificationService;
        private readonly INotificationRepository _notificationRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;
        private readonly TelemetryClient _telemetryClient;

        public NotifierService(
            ILoggingRepository loggingRepository,
            IMailRepository mailRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            ILocalizationRepository localizationRepository,
            IThresholdNotificationService thresholdNotificationService,
            INotificationRepository notificationRepository,
            IGraphGroupRepository graphGroupRepository,
            TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _thresholdNotificationService = thresholdNotificationService ?? throw new ArgumentNullException(nameof(thresholdNotificationService));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        public async Task SendEmailAsync(ThresholdNotification notification)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sending email to recipient addresses." });

            var groupName = await _graphGroupRepository.GetGroupNameAsync(notification.TargetOfficeGroupId);
            var owners = await _graphGroupRepository.GetGroupOwnersAsync(notification.TargetOfficeGroupId);
            var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));

            var adaptiveCard = await _thresholdNotificationService.CreateNotificationCardAsync(notification);
            var htmlTemplate = @"<html>
                <head
                  <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
                  <script type=""application/adaptivecard+json"">
                 {0}
                  </script>
                </head>
                <body>
                </body>
                </html>";

            var message = new EmailMessage
            {
                Subject = _localizationRepository.TranslateSetting("SyncThresholdEmailSubject", groupName),
                Content = string.Format(htmlTemplate, adaptiveCard),
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = ownerEmails,
                CcEmailAddresses = _emailSenderAndRecipients.SupportEmailAddresses,
                IsHTML = true
            };
            
            await _mailRepository.SendMailAsync(message, null);
            TrackSentNotificationEvent(notification.TargetOfficeGroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sent email to recipient addresses." });
        }

        public async Task<List<Models.ThresholdNotifications.ThresholdNotification>> RetrieveQueuedNotificationsAsync()
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

        public async Task UpdateNotificationStatusAsync(Models.ThresholdNotifications.ThresholdNotification notification, ThresholdNotificationStatus status)
        {
            await _notificationRepository.UpdateNotificationStatusAsync(notification, status);
        }

        private void TrackSentNotificationEvent(Guid groupId)
        {
            var sentNotificationEvent = new Dictionary<string, string>
            {
                { "TargetGroupId", groupId.ToString() }
            };
            _telemetryClient.TrackEvent("NotificationSent", sentNotificationEvent);
        }

        public async Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotificationFromContentAsync(Dictionary<string, object> messageContent)
        {
            ThresholdResult threshold = JsonConvert.DeserializeObject<ThresholdResult>(messageContent["ThresholdResult"].ToString());
            SyncJob job = JsonConvert.DeserializeObject<SyncJob>(messageContent["SyncJob"].ToString());
            bool sendDisableJobNotification = Convert.ToBoolean(messageContent["SendDisableJobNotification"]);
            var notification = await CreateActionableNotification(threshold, job, sendDisableJobNotification);
            return notification;
        }
        private async Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotification(ThresholdResult threshold, SyncJob job, bool sendDisableJobNotification)
        {
            var thresholdNotification = await _notificationRepository.GetThresholdNotificationBySyncJobIdAsync(job.Id);

            if (thresholdNotification == null)
            {
                thresholdNotification = new ThresholdNotification
                {
                    Id = Guid.NewGuid(),
                    SyncJobPartitionKey = job.Id.ToString(),
                    SyncJobRowKey = job.Id.ToString(),
                    SyncJobId = job.Id,
                    ChangePercentageForAdditions = (int)threshold.IncreaseThresholdPercentage,
                    ChangePercentageForRemovals = (int)threshold.DecreaseThresholdPercentage,
                    ChangeQuantityForAdditions = threshold.DeltaToAddCount,
                    ChangeQuantityForRemovals = threshold.DeltaToRemoveCount,
                    CreatedTime = DateTime.UtcNow,
                    Resolution = ThresholdNotificationResolution.Unresolved,
                    ResolvedByUPN = string.Empty,
                    ResolvedTime = DateTime.FromFileTimeUtc(0),
                    Status = ThresholdNotificationStatus.Triggered,
                    CardState = ThresholdNotificationCardState.DefaultCard,
                    TargetOfficeGroupId = job.TargetOfficeGroupId,
                    ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                    ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals
                };
            }
            else
            {
                thresholdNotification.ChangePercentageForAdditions = (int)threshold.IncreaseThresholdPercentage;
                thresholdNotification.ChangePercentageForRemovals = (int)threshold.DecreaseThresholdPercentage;
                thresholdNotification.ChangeQuantityForAdditions = threshold.DeltaToAddCount;
                thresholdNotification.ChangeQuantityForRemovals = threshold.DeltaToRemoveCount;
                thresholdNotification.ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions;
                thresholdNotification.ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals;
                thresholdNotification.Status = ThresholdNotificationStatus.Triggered;

                if (sendDisableJobNotification)
                {
                    thresholdNotification.CardState = ThresholdNotificationCardState.DisabledCard;
                }
            }

            await _notificationRepository.SaveNotificationAsync(thresholdNotification);
            return thresholdNotification;
        }
    }
}
