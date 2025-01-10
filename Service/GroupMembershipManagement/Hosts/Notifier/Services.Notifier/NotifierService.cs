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
using System.Net.Http;
using System.Net;
using Models.ServiceBus;

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
        private readonly IThresholdConfig _thresholdConfig;
        private readonly IGMMResources _gmmResources;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;

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
            IThresholdConfig thresholdConfig,
            IGMMResources gmmResources,
            IServiceBusQueueRepository serviceBusQueueRepository,
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
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
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
            _gmmResources = gmmResources ?? throw new ArgumentException(nameof(gmmResources));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(_serviceBusQueueRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return channel.GroupId;
            }
            else if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var group = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                return group.GroupId;
            }
            return Guid.Empty;
        }

        public async Task<string> GetChannelIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return channel.ChannelId;
            }
            return string.Empty;
        }

        public async Task SendThresholdEmailAsync(ThresholdNotification notification)
        {
            bool isNotificationDisabled = await IsNotificationDisabledAsync(notification.SyncJobId, NotificationMessageType.ThresholdNotification);

            if (isNotificationDisabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = notification.SyncJobId,
                    Message = $"Notification '{NotificationMessageType.ThresholdNotification}' is disabled for job {notification.Id} with destination group {notification.TargetOfficeGroupId}."
                });
                return;
            }
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sending email to recipient addresses." });

            var groupName = await _graphGroupRepository.GetGroupNameAsync(notification.TargetOfficeGroupId);
            var owners = await _graphGroupRepository.GetGroupOwnersAsync(notification.TargetOfficeGroupId);
            var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));

            var adaptiveCard = await _thresholdNotificationService.CreateNotificationCardAsync(notification);

            var fallbackHTMLContent = _localizationRepository.TranslateSetting(NotificationConstants.ThresholdNotificationFallbackBody,
                groupName,
                notification.TargetOfficeGroupId.ToString(),
                notification.ThresholdPercentageForAdditions.ToString(),
                notification.ThresholdPercentageForRemovals.ToString());
            
            var htmlTemplate = @"<html>
                <head
                  <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
                  <script type=""application/adaptivecard+json"">
                 {0}
                  </script>
                </head>
                <body>
                <p style=""color: red;"">Warning: Group Membership Management (GMM) notifications are powered by Outlook Actionable Messages. The following is a fallback message that you will see if the Actionable Message fails to render.</p>
                <h1>Fallback Message</h1>
                <pre>{1}</pre>
                </body>
                </html>";

            var cardState = notification.CardState; 

            string subjectKey = cardState == ThresholdNotificationCardState.DisabledCard 
                ? "SyncThresholdDisablingJobEmailSubject" 
                : "SyncThresholdEmailSubject";

            string subject = _localizationRepository.TranslateSetting(subjectKey, groupName);
            
            var message = new EmailMessage
            {
                Subject = subject,
                Content = string.Format(htmlTemplate, adaptiveCard, fallbackHTMLContent),
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = ownerEmails,
                CcEmailAddresses = _emailSenderAndRecipients.SupportEmailAddresses,
                IsHTML = true
            };

            var response = await _mailRepository.SendMailAsync(message, null);
            TrackSentNotificationEvent(notification.TargetOfficeGroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sent email to recipient addresses." });

            if (response != null && response.StatusCode != HttpStatusCode.Accepted)
            {
                var messageContent = new Dictionary<string, Object>
                {
                    { "MessageBody", message.Content },
                    { "MessageType", NotificationMessageType.ThresholdNotification.ToString() },
                    { "HttpStatusCode", response.StatusCode.ToString() },
                    { "ReasonPhrase", response.ReasonPhrase.ToString() }
                };
                var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
                var failedMessage = new ServiceBusMessage
                {
                    MessageId = $"{notification.Id}_{notification.SyncJobId}_{NotificationMessageType.ThresholdNotification}",
                    Body = body
                };
                await _serviceBusQueueRepository.SendMessageAsync(failedMessage);
            }
            TrackSentNotificationEvent(notification.TargetOfficeGroupId);
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
        private (SyncJob job, string[] additionalContentParameters) ParseMessageContentAsync(string messageBody)
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
        public async Task SendEmailAsync(string messageType, string messageBody, string messageTitle, string subjectTemplate, string contentTemplate)
        {
            var (job, additionalContentParameters) = ParseMessageContentAsync(messageBody);
            var groupId = await GetGroupIdAsync(job);

            if (!Enum.TryParse<NotificationMessageType>(messageType, true, out var messageTypeEnum))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = $"Notification type '{messageType}' do not exist."
                });
                return;
            }
            bool isNotificationDisabled = await IsNotificationDisabledAsync(job.Id, messageTypeEnum);

            if (isNotificationDisabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = $"Notification '{messageType}' is disabled for job {job.Id} with destination group {groupId}."
                });
                return;
            }
            string ownerEmails = null;
            string ccAddress = _emailSenderAndRecipients.SupportEmailAddresses;

            if (!NotificationConstants.DestinationNotExistContent.Equals(contentTemplate, StringComparison.InvariantCultureIgnoreCase))
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(groupId);
                ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
            }

            var message = new EmailMessage
            {
                Title = messageTitle,
                Subject = subjectTemplate,
                Content = contentTemplate,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = ownerEmails ?? job.Requestor,
                CcEmailAddresses = ccAddress,
                AdditionalContentParams = additionalContentParameters,
                SyncJobId = job.Id
            };

            if (messageType.Equals("NoDataNotification", StringComparison.InvariantCultureIgnoreCase))
            {
                message.AdditionalSubjectParams = additionalContentParameters;
            }
            var response =  await _mailRepository.SendMailAsync(message, job.RunId);

            if (response != null && response.StatusCode != HttpStatusCode.Accepted) {
                var messageContent = new Dictionary<string, Object>
                {
                    { "MessageBody", messageBody },
                    { "MessageType", messageType },
                    { "HttpStatusCode", response.StatusCode.ToString() },
                    { "ReasonPhrase", response.ReasonPhrase.ToString() }
                };
                var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
                var failedMessage = new ServiceBusMessage
                {
                    MessageId = $"{job.Id}_{job.RunId}_{messageType}",
                    Body = body
                };
                await _serviceBusQueueRepository.SendMessageAsync(failedMessage);
            }
        }

        public async Task<bool> IsNotificationDisabledAsync(Guid jobId, NotificationMessageType messageType)
        {
            var notificationType = await _notificationTypesRepository.GetNotificationTypeByNotificationTypeNameAsync(messageType);

            if (notificationType == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = jobId,
                    Message = $"No notification type ID found for notification type name '{messageType}'."
                });
                return false;
            }

            if (notificationType.Disabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = jobId,
                    Message = $"Notifications of type '{messageType}' have been globally disabled."
                });
                return true;
            }

            return await _jobNotificationRepository.IsNotificationDisabledForJobAsync(jobId, notificationType.Id);
        }

        public async Task<AzureADGroup> ParseDestinationAsync(SyncJob syncJob)
        {
            var groupId = await GetGroupIdAsync(syncJob);
            var channelId = await GetChannelIdAsync(syncJob);

            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                return new AzureADTeamsChannel
                {
                    Type = syncJob.MembershipType,
                    ObjectId = groupId,
                    ChannelId = channelId
                };
            }
            else if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                return new AzureADGroup
                {
                    Type = syncJob.MembershipType,
                    ObjectId = groupId
                };
            }
            else
            {
                return null;
            }
        }
        private async Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotification(ThresholdResult threshold, SyncJob job, bool sendDisableJobNotification)
        {
            var groupId = await GetGroupIdAsync(job);
            var thresholdNotification = await _notificationRepository.GetThresholdNotificationBySyncJobIdAsync(job.Id);

            if (thresholdNotification == null)
            {
                thresholdNotification = new ThresholdNotification
                {
                    Id = Guid.NewGuid(),
                    SyncJobId = job.Id,
                    ChangePercentageForAdditions = threshold.IncreaseThresholdPercentage,
                    ChangePercentageForRemovals = threshold.DecreaseThresholdPercentage,
                    ChangeQuantityForAdditions = threshold.DeltaToAddCount,
                    ChangeQuantityForRemovals = threshold.DeltaToRemoveCount,
                    CreatedTime = DateTime.UtcNow,
                    Resolution = ThresholdNotificationResolution.Unresolved,
                    ResolvedBy = string.Empty,
                    ResolvedTime = DateTime.FromFileTimeUtc(0),
                    Status = ThresholdNotificationStatus.Triggered,
                    CardState = ThresholdNotificationCardState.DefaultCard,
                    TargetOfficeGroupId = groupId,
                    ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                    ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals
                };
            }
            else
            {
                thresholdNotification.ChangePercentageForAdditions = threshold.IncreaseThresholdPercentage;
                thresholdNotification.ChangePercentageForRemovals = threshold.DecreaseThresholdPercentage;
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
        private (SyncJob job, ThresholdResult threshold, bool sendDisableJobNotification, string groupName) ParseNormalThresholdMessageContent(string messageBody)
        {
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, Object>>(messageBody);
            SyncJob job = ((JsonElement)messageContent["SyncJob"]).Deserialize<SyncJob>();
            ThresholdResult threshold = ((JsonElement)messageContent["ThresholdResult"]).Deserialize<ThresholdResult>();
            bool sendDisableJobNotification = ((JsonElement)messageContent["SendDisableJobNotification"]).Deserialize<bool>();
            string groupName = ((JsonElement)messageContent["GroupName"]).GetString();
            return (job, threshold, sendDisableJobNotification, groupName);
        }

        public async Task SendNormalThresholdEmailAsync(string messageBody)
        {
            var (job, threshold, sendDisableJobNotification, groupName) = ParseNormalThresholdMessageContent(messageBody);
            var groupId = await GetGroupIdAsync(job);
            bool isNotificationDisabled = await IsNotificationDisabledAsync(job.Id, NotificationMessageType.NormalThresholdNotification);

            if (isNotificationDisabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = $"Notification '{NotificationMessageType.NormalThresholdNotification}' is disabled for job {job.Id} with destination group {groupId}."
                });
                return;
            }
            var emailSubject = NotificationConstants.SyncThresholdEmailSubject;

            string contentTemplate;
            string[] additionalContent;
            string[] additionalSubjectContent = new[] { groupName };

            var thresholdEmail = GetNormalThresholdEmail(groupName, threshold, job, groupId);
            contentTemplate = thresholdEmail.ContentTemplate;
            additionalContent = thresholdEmail.AdditionalContent;

            var recipients = _emailSenderAndRecipients.SupportEmailAddresses;

            if (!string.IsNullOrWhiteSpace(job.Requestor))
            {
                var recipientList = await GetThresholdRecipientsAsync(job.Requestor, groupId);
                if (recipientList.Count > 0)
                    recipients = string.Join(",", recipientList);
            }

            if (sendDisableJobNotification)
            {
                emailSubject = NotificationConstants.SyncThresholdDisablingJobEmailSubject;
                contentTemplate = NotificationConstants.SyncJobDisabledEmailBody;
                additionalContent = new[]
                {
                    groupName,
                    groupId.ToString(),
                    _emailSenderAndRecipients.SupportEmailAddresses,
                    _gmmResources.LearnMoreAboutGMMUrl
                };
            }
            var message = new EmailMessage
            {
                Subject = emailSubject,
                Content = contentTemplate,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = recipients,
                CcEmailAddresses = _emailSenderAndRecipients.SupportEmailAddresses,
                AdditionalContentParams = additionalContent,
                AdditionalSubjectParams = additionalSubjectContent,
                SyncJobId = job.Id
            };
            var response = await _mailRepository.SendMailAsync(message, job.RunId);

            if (response != null && response.StatusCode != HttpStatusCode.Accepted)
            {
                var messageContent = new Dictionary<string, Object>
                {
                    { "MessageBody", messageBody },
                    { "MessageType", NotificationMessageType.NormalThresholdNotification },
                    { "HttpStatusCode", response.StatusCode.ToString() },
                    { "ReasonPhrase", response.ReasonPhrase.ToString() }
                };
                var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
                var failedMessage = new ServiceBusMessage
                {
                    MessageId = $"{job.Id}_{job.RunId}_{NotificationMessageType.NormalThresholdNotification}",
                    Body = body
                };
                await _serviceBusQueueRepository.SendMessageAsync(failedMessage);
            }
        }
        private (string ContentTemplate, string[] AdditionalContent) GetNormalThresholdEmail(string groupName, ThresholdResult threshold, SyncJob job, Guid groupId)
        {
            string increasedThresholdMessage;
            string decreasedThresholdMessage;
            string contentTemplate = NotificationConstants.SyncThresholdBothEmailBody;
            string[] additionalContent;

            increasedThresholdMessage = _localizationRepository.TranslateSetting(
                                                        NotificationConstants.IncreaseThresholdMessage,
                                                        job.ThresholdPercentageForAdditions.ToString(),
                                                        threshold.IncreaseThresholdPercentage.ToString("F2"));

            decreasedThresholdMessage = _localizationRepository.TranslateSetting(
                                               NotificationConstants.DecreaseThresholdMessage,
                                               job.ThresholdPercentageForRemovals.ToString(),
                                               threshold.DecreaseThresholdPercentage.ToString("F2"));

            if (threshold.IsAdditionsThresholdExceeded && threshold.IsRemovalsThresholdExceeded)
            {
                additionalContent = new[]
                {
                      groupId.ToString(),
                      groupName,
                      $"{increasedThresholdMessage}\n{decreasedThresholdMessage}",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                };
            }
            else if (threshold.IsAdditionsThresholdExceeded)
            {
                additionalContent = new[]
                {
                      groupId.ToString(),
                      groupName,
                      $"{increasedThresholdMessage}\n",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                    };
            }
            else
            {
                additionalContent = new[]
                {
                      groupId.ToString(),
                      groupName,
                      $"{decreasedThresholdMessage}\n",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                };
            }

            return (contentTemplate, additionalContent);
        }
        private async Task<List<string>> GetThresholdRecipientsAsync(string requestors, Guid targetOfficeGroupId)
        {
            var recipients = new List<string>();
            var emails = requestors.Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

            foreach (var email in emails)
            {
                if (await IsEmailRecipientOwnerOfGroupAsync(email, targetOfficeGroupId))
                {
                    recipients.Add(email);
                }
            }

            if (recipients.Count > 0) return recipients;

            var top = _thresholdConfig.MaximumNumberOfThresholdRecipients > 0 ? _thresholdConfig.MaximumNumberOfThresholdRecipients + 1 : 0;
            var owners = await GetGroupOwnersAsync(targetOfficeGroupId, top);

            if (owners.Count <= _thresholdConfig.MaximumNumberOfThresholdRecipients || _thresholdConfig.MaximumNumberOfThresholdRecipients == 0)
            {
                recipients.AddRange(owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
            }

            return recipients;
        }
        private async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            return await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(email, groupObjectId);
        }
        private async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupRepository.GetGroupOwnersAsync(groupObjectId, top);
        }
    }
}
