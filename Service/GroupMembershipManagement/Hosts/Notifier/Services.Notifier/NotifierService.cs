// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Notifications;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Services.Notifier.Contracts;
using System;
using System.Linq;
using Repositories.Contracts.InjectConfig;
using Models.ThresholdNotifications;
using Services.Contracts.Notifications;
using Services.Contracts;
using Microsoft.ApplicationInsights;
using System.Text.Json;
using Models.Entities;

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
        private readonly INotificationTypesRepository _notificationTypesRepository;
        private readonly IJobNotificationsRepository _jobNotificationRepository;

        public NotifierService(
            ILoggingRepository loggingRepository,
            IMailRepository mailRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            ILocalizationRepository localizationRepository,
            IThresholdNotificationService thresholdNotificationService,
            INotificationRepository notificationRepository,
            IGraphGroupRepository graphGroupRepository,
            INotificationTypesRepository notificationTypesRepository,
            IJobNotificationsRepository jobNotificationRepository,
            TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _thresholdNotificationService = thresholdNotificationService ?? throw new ArgumentNullException(nameof(thresholdNotificationService));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _notificationTypesRepository = notificationTypesRepository ?? throw new ArgumentNullException(nameof(notificationTypesRepository));
            _jobNotificationRepository = jobNotificationRepository ?? throw new ArgumentNullException(nameof(jobNotificationRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        public async Task SendThresholdEmailAsync(ThresholdNotification notification)
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

        public async Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotificationFromContentAsync(string messageBody)
        {
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, Object>>(messageBody);
            SyncJob job = ((JsonElement)messageContent["SyncJob"]).Deserialize<SyncJob>();
            ThresholdResult threshold = ((JsonElement)messageContent["ThresholdResult"]).Deserialize<ThresholdResult>();
            bool sendDisableJobNotification = ((JsonElement)messageContent["SendDisableJobNotification"]).Deserialize<bool>();
            var notification = await CreateActionableNotification(threshold, job, sendDisableJobNotification);
            return notification;
        }
        private async Task<(SyncJob job, string[] additionalContentParameters)> ParseMessageContentAsync(string messageBody)
        {
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, object>>(messageBody);

            SyncJob job = ((JsonElement)messageContent["SyncJob"]).Deserialize<SyncJob>();

            string[] additionalContentParameters = Array.Empty<string>();
            if (messageContent.ContainsKey("AdditionalContentParameters"))
            {
                var additionalContentJsonElement = (JsonElement)messageContent["AdditionalContentParameters"];
                if (additionalContentJsonElement.ValueKind == JsonValueKind.Array)
                {
                    additionalContentParameters = additionalContentJsonElement.Deserialize<string[]>();
                }
            }

            return (job, additionalContentParameters);
        }
        public async Task SendEmailAsync(string messageType, string messageBody, string subjectTemplate, string contentTemplate)
        {
            var (job, additionalContentParameters) = await ParseMessageContentAsync(messageBody);

            bool isNotificationDisabled = await IsNotificationDisabledAsync(job.Id, contentTemplate);

            if (isNotificationDisabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = $"Notification template '{contentTemplate}' is disabled for job {job.Id} with destination group {job.TargetOfficeGroupId}."
                });
                return;
            }
            string ownerEmails = null;
            string ccAddress = _emailSenderAndRecipients.SupportEmailAddresses;

            if (!NotificationConstants.DestinationNotExistContent.Equals(contentTemplate, StringComparison.InvariantCultureIgnoreCase))
            {
                var destinationObjectId = (await ParseDestinationAsync(job)).ObjectId;
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(destinationObjectId);
                ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
            }

            if (contentTemplate.Contains("disabled", StringComparison.InvariantCultureIgnoreCase))
                ccAddress = _emailSenderAndRecipients.SyncDisabledCCAddresses;

            var message = new EmailMessage
            {
                Subject = subjectTemplate,
                Content = contentTemplate,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = ownerEmails ?? job.Requestor,
                CcEmailAddresses = ccAddress,
                AdditionalContentParams = additionalContentParameters
            };

            await _mailRepository.SendMailAsync(message, job.RunId);
        }

        public async Task<bool> IsNotificationDisabledAsync(Guid jobId, string contentTemplate)
        {
            var notificationType = await _notificationTypesRepository.GetNotificationTypeByNotificationTypeNameAsync(contentTemplate);

            if (notificationType == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = jobId,
                    Message = $"No notification type ID found for notification type name '{contentTemplate}'."
                });
                return false;
            }

            if (notificationType.Disabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = jobId,
                    Message = $"Notifications of type '{contentTemplate}' have been globally disabled."
                });
                return true;
            }

            return await _jobNotificationRepository.IsNotificationDisabledForJobAsync(jobId, notificationType.Id);
        }

        public async Task<AzureADGroup> ParseDestinationAsync(SyncJob syncJob)
        {
            if (string.IsNullOrWhiteSpace(syncJob.Destination)) return null;

            using JsonDocument doc = JsonDocument.Parse(syncJob.Destination);
            JsonElement rootElement = doc.RootElement[0];

            if (rootElement.ValueKind != JsonValueKind.Object) return null;

            JsonElement valueElement;
            if (!rootElement.TryGetProperty("value", out valueElement) ||
                !rootElement.TryGetProperty("type", out JsonElement typeElement) ||
                valueElement.ValueKind != JsonValueKind.Object ||
                !valueElement.TryGetProperty("objectId", out JsonElement objectIdElement) ||
                !Guid.TryParse(objectIdElement.GetString(), out Guid objectIdGuid))
            {
                return null;
            }

            string type = typeElement.GetString();

            if (type == "TeamsChannelMembership")
            {
                if (!valueElement.TryGetProperty("channelId", out JsonElement channelIdElement)) return null;

                return new AzureADTeamsChannel
                {
                    Type = type,
                    ObjectId = objectIdGuid,
                    ChannelId = channelIdElement.GetString()
                };
            }
            else if (type == "GroupMembership")
            {
                return new AzureADGroup
                {
                    Type = type,
                    ObjectId = objectIdGuid
                };
            }
            else
            {
                return null;
            }
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
