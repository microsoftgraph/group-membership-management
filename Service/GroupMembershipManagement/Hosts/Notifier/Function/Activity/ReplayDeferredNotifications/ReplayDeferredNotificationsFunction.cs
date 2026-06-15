// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Metrics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.Notifier
{
    public class ReplayDeferredNotificationsFunction
    {
        private readonly ILogger<ReplayDeferredNotificationsFunction> _logger;
        private readonly INotificationTypesRepository _notificationTypesRepository;
        private readonly IDeferredNotificationsRepository _deferredNotificationsRepository;
        private readonly IMailConfig _mailConfig;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _topicName;
        private readonly string _subscriptionName;

        private const int BatchSize = 50;
        private static readonly TimeSpan ExpiryWarningThreshold = TimeSpan.FromHours(24);

        public ReplayDeferredNotificationsFunction(
            ILogger<ReplayDeferredNotificationsFunction> logger,
            INotificationTypesRepository notificationTypesRepository,
            IDeferredNotificationsRepository deferredNotificationsRepository,
            IMailConfig mailConfig,
            ServiceBusClient serviceBusClient,
            TelemetryClient telemetryClient,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationTypesRepository = notificationTypesRepository ?? throw new ArgumentNullException(nameof(notificationTypesRepository));
            _deferredNotificationsRepository = deferredNotificationsRepository ?? throw new ArgumentNullException(nameof(deferredNotificationsRepository));
            _mailConfig = mailConfig ?? throw new ArgumentNullException(nameof(mailConfig));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _topicName = configuration["serviceBusNotificationsTopic"] ?? throw new ArgumentNullException("serviceBusNotificationsTopic");
            _subscriptionName = configuration["serviceBusNotificationsSubscription"] ?? throw new ArgumentNullException("serviceBusNotificationsSubscription");
        }

        [Function(nameof(ReplayDeferredNotificationsFunction))]
        public async Task RunAsync([TimerTrigger("%notifierReplaySchedule%")] TimerInfo timer)
        {
            _logger.FunctionStarted(nameof(ReplayDeferredNotificationsFunction));

            // Item 6: If global email suppression is still active, skip replay entirely
            // to avoid sending messages back to the topic where they'd be immediately re-deferred.
            if (_mailConfig.SkipEmailNotifications)
            {
                _logger.ReplaySkippedGlobalSuppression();
                await EmitObservabilityMetricsAsync();
                _logger.FunctionCompleted(nameof(ReplayDeferredNotificationsFunction));
                return;
            }

            var notificationTypes = Enum.GetValues<NotificationMessageType>();

            foreach (var messageType in notificationTypes)
            {
                var notificationType = await _notificationTypesRepository.GetNotificationTypeByNotificationTypeNameAsync(messageType);

                if (notificationType == null || notificationType.Disabled)
                    continue;

                var deferredMessages = await _deferredNotificationsRepository
                    .GetDeferredNotificationsByTypeAndStatusAsync(messageType, DeferredNotificationStatus.Deferred);

                if (!deferredMessages.Any())
                    continue;

                // Item 4: Alert on messages approaching TTL expiry
                WarnOnApproachingExpiry(messageType, deferredMessages);

                // Mark expired messages before attempting replay
                var expiredMessages = deferredMessages
                    .Where(d => d.MessageExpiresAt.HasValue && d.MessageExpiresAt.Value <= DateTime.UtcNow)
                    .ToList();

                if (expiredMessages.Any())
                {
                    _logger.DeferredMessagesExpired(messageType.ToString(), expiredMessages.Count);

                    await _deferredNotificationsRepository.UpdateStatusBatchAsync(
                        expiredMessages.Select(d => d.Id), DeferredNotificationStatus.Expired);

                    _telemetryClient.TrackEvent("NotificationsExpired", new Dictionary<string, string>
                    {
                        { "MessageType", messageType.ToString() },
                        { "Count", expiredMessages.Count.ToString() }
                    });
                }

                var replayableMessages = deferredMessages.Except(expiredMessages).ToList();
                if (!replayableMessages.Any())
                    continue;

                _logger.ReplayingDeferredNotifications(messageType.ToString(), replayableMessages.Count);

                try
                {
                    await ReplayMessagesAsync(messageType, replayableMessages);
                }
                catch (Exception ex)
                {
                    // Log and continue with other types; recovery already attempted inside ReplayMessagesAsync
                    _logger.DeferredMessageReplayFailed(0, messageType.ToString(),
                        $"Replay failed for type: {ex.Message}");
                }
            }

            // Item 7: Emit aggregate observability metrics
            await EmitObservabilityMetricsAsync();

            _logger.FunctionCompleted(nameof(ReplayDeferredNotificationsFunction));
        }

        private async Task ReplayMessagesAsync(
            NotificationMessageType messageType, IList<DeferredNotification> deferredMessages)
        {
            // Mark all as Replaying to prevent concurrent replay attempts
            await _deferredNotificationsRepository.UpdateStatusBatchAsync(
                deferredMessages.Select(d => d.Id), DeferredNotificationStatus.Replaying);

            int replayedCount = 0;
            int failedCount = 0;

            try
            {
                await using var receiver = _serviceBusClient.CreateReceiver(_topicName, _subscriptionName);
                await using var sender = _serviceBusClient.CreateSender(_topicName);

                var sequenceNumbers = deferredMessages.Select(d => d.SequenceNumber).ToArray();

                for (int i = 0; i < sequenceNumbers.Length; i += BatchSize)
                {
                    var batch = sequenceNumbers.Skip(i).Take(BatchSize).ToArray();
                    var batchDeferredIds = deferredMessages
                        .Where(d => batch.Contains(d.SequenceNumber))
                        .ToDictionary(d => d.SequenceNumber, d => d.Id);

                    IReadOnlyList<ServiceBusReceivedMessage> messages;
                    try
                    {
                        messages = await receiver.ReceiveDeferredMessagesAsync(batch);
                    }
                    catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageNotFound)
                    {
                        _logger.DeferredMessageNotFound(messageType.ToString(), ex.Message);

                        await _deferredNotificationsRepository.UpdateStatusBatchAsync(
                            batchDeferredIds.Values, DeferredNotificationStatus.Expired);
                        failedCount += batchDeferredIds.Count;
                        continue;
                    }

                    foreach (var msg in messages)
                    {
                        try
                        {
                            // Use a stable, deterministic message ID for duplicate detection.
                            // Format: {originalMessageId}_replay_{sequenceNumber}
                            // This ensures the same deferred message always produces the same replay ID,
                            // preventing duplicates if replay is retried.
                            var replayMessage = new ServiceBusMessage(msg)
                            {
                                MessageId = $"{msg.MessageId}_replay_{msg.SequenceNumber}"
                            };
                            await sender.SendMessageAsync(replayMessage);
                            await receiver.CompleteMessageAsync(msg);

                            // Per-message status update after confirmed success
                            if (batchDeferredIds.TryGetValue(msg.SequenceNumber, out var deferredId))
                            {
                                await _deferredNotificationsRepository.UpdateStatusAsync(
                                    deferredId, DeferredNotificationStatus.Replayed);
                            }

                            replayedCount++;
                            _logger.DeferredMessageReplayed(msg.SequenceNumber, messageType.ToString());
                        }
                        catch (ServiceBusException ex)
                        {
                            _logger.DeferredMessageReplayFailed(msg.SequenceNumber, messageType.ToString(), ex.Message);

                            // Reset to Deferred so it's retried on next timer run
                            if (batchDeferredIds.TryGetValue(msg.SequenceNumber, out var failedId))
                            {
                                await _deferredNotificationsRepository.UpdateStatusAsync(
                                    failedId, DeferredNotificationStatus.Deferred);
                            }
                            failedCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // On unexpected exceptions, reset only rows still in Replaying status.
                // Rows already marked Replayed were successfully completed in Service Bus
                // and must not be reverted.
                _logger.DeferredMessageReplayFailed(0, messageType.ToString(),
                    $"Unexpected error during replay: {ex.Message}");

                try
                {
                    var stillReplayingIds = (await _deferredNotificationsRepository
                        .GetDeferredNotificationsByTypeAndStatusAsync(messageType, DeferredNotificationStatus.Replaying))
                        .Select(d => d.Id)
                        .Intersect(deferredMessages.Select(d => d.Id))
                        .ToList();

                    if (stillReplayingIds.Any())
                    {
                        await _deferredNotificationsRepository.UpdateStatusBatchAsync(
                            stillReplayingIds, DeferredNotificationStatus.Deferred);
                    }
                }
                catch
                {
                    // Best-effort recovery; rows may remain in Replaying but will be
                    // picked up by a future reconciliation or manual intervention
                }

                throw;
            }

            // Clean up successfully replayed rows
            await _deferredNotificationsRepository.RemoveDeferredNotificationsByTypeAndStatusAsync(
                messageType, DeferredNotificationStatus.Replayed);

            // Item 7: Track replay outcome metrics
            _telemetryClient.TrackEvent("NotificationsReplayed", new Dictionary<string, string>
            {
                { "MessageType", messageType.ToString() },
                { "ReplayedCount", replayedCount.ToString() },
                { "FailedCount", failedCount.ToString() }
            });

            _telemetryClient.GetMetric("NotifierReplaySuccessCount", "MessageType")
                .TrackValue(replayedCount, messageType.ToString());
            _telemetryClient.GetMetric("NotifierReplayFailureCount", "MessageType")
                .TrackValue(failedCount, messageType.ToString());

            _logger.ReplayCompleted(messageType.ToString(), replayedCount);
        }

        /// <summary>
        /// Item 4: Warn when deferred messages are approaching their Service Bus TTL expiry.
        /// </summary>
        private void WarnOnApproachingExpiry(NotificationMessageType messageType, IList<DeferredNotification> messages)
        {
            var approachingExpiry = messages
                .Where(d => d.MessageExpiresAt.HasValue
                    && d.MessageExpiresAt.Value > DateTime.UtcNow
                    && d.MessageExpiresAt.Value <= DateTime.UtcNow.Add(ExpiryWarningThreshold))
                .ToList();

            if (approachingExpiry.Any())
            {
                var oldestExpiry = approachingExpiry.Min(d => d.MessageExpiresAt!.Value);
                var hoursUntilExpiry = (oldestExpiry - DateTime.UtcNow).TotalHours;

                _logger.DeferredMessagesApproachingExpiry(
                    messageType.ToString(), approachingExpiry.Count, hoursUntilExpiry);

                _telemetryClient.TrackEvent("NotificationsApproachingExpiry", new Dictionary<string, string>
                {
                    { "MessageType", messageType.ToString() },
                    { "Count", approachingExpiry.Count.ToString() },
                    { "HoursUntilOldestExpiry", hoursUntilExpiry.ToString("F1") }
                });
            }
        }

        /// <summary>
        /// Item 7: Emit aggregate metrics for dashboards and alerting.
        /// </summary>
        private async Task EmitObservabilityMetricsAsync()
        {
            try
            {
                var notificationTypes = Enum.GetValues<NotificationMessageType>();

                foreach (var messageType in notificationTypes)
                {
                    var deferred = await _deferredNotificationsRepository
                        .GetDeferredNotificationsByTypeAndStatusAsync(messageType, DeferredNotificationStatus.Deferred);

                    if (!deferred.Any())
                        continue;

                    // Deferred count per type
                    _telemetryClient.GetMetric("NotifierDeferredCount", "MessageType")
                        .TrackValue(deferred.Count, messageType.ToString());

                    // Oldest deferred age in hours
                    var oldestAge = (DateTime.UtcNow - deferred.Min(d => d.DeferredAt)).TotalHours;
                    _telemetryClient.GetMetric("NotifierOldestDeferredAgeHours", "MessageType")
                        .TrackValue(oldestAge, messageType.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.ObservabilityMetricsError(ex.Message);
            }
        }
    }
}
