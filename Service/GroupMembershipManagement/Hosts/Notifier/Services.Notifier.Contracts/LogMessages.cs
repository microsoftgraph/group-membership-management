// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.Notifier
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 80000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 80001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction ──

        [LoggerMessage(EventId = 80002, Level = LogLevel.Information,
            Message = "Email notifications are disabled.")]
        public static partial void EmailNotificationsDisabled(this ILogger logger);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 80010, Level = LogLevel.Warning,
            Message = "{MessageType} is not a valid message type")]
        public static partial void InvalidMessageType(this ILogger logger, string messageType);

        // ── NotifierService ──

        [LoggerMessage(EventId = 80080, Level = LogLevel.Information,
            Message = "Notification '{NotificationType}' is disabled for job {JobId} with destination group {GroupId}.")]
        public static partial void NotificationDisabled(this ILogger logger, string notificationType, Guid jobId, Guid groupId);

        [LoggerMessage(EventId = 80081, Level = LogLevel.Information,
            Message = "Sending email to recipient addresses.")]
        public static partial void SendingEmail(this ILogger logger);

        [LoggerMessage(EventId = 80082, Level = LogLevel.Information,
            Message = "Sent email to recipient addresses.")]
        public static partial void SentEmail(this ILogger logger);

        [LoggerMessage(EventId = 80083, Level = LogLevel.Warning,
            Message = "Notification type '{NotificationType}' do not exist.")]
        public static partial void NotificationTypeNotExist(this ILogger logger, string notificationType);

        [LoggerMessage(EventId = 80084, Level = LogLevel.Warning,
            Message = "No notification type ID found for notification type name '{NotificationType}'.")]
        public static partial void NoNotificationTypeIdFound(this ILogger logger, string notificationType);

        [LoggerMessage(EventId = 80085, Level = LogLevel.Information,
            Message = "Notifications of type '{NotificationType}' have been globally disabled.")]
        public static partial void NotificationsGloballyDisabled(this ILogger logger, string notificationType);

        [LoggerMessage(EventId = 80086, Level = LogLevel.Warning,
            Message = "Notification type '{MessageType}' is suppressed. Message with sequence number {SequenceNumber} has been deferred.")]
        public static partial void NotificationTypeSuppressed(this ILogger logger, string messageType, long sequenceNumber);

        // ── ReplayDeferredNotificationsFunction ──

        [LoggerMessage(EventId = 80090, Level = LogLevel.Information,
            Message = "Replaying {Count} deferred notifications of type '{MessageType}'.")]
        public static partial void ReplayingDeferredNotifications(this ILogger logger, string messageType, int count);

        [LoggerMessage(EventId = 80091, Level = LogLevel.Information,
            Message = "Deferred message with sequence number {SequenceNumber} of type '{MessageType}' has been replayed.")]
        public static partial void DeferredMessageReplayed(this ILogger logger, long sequenceNumber, string messageType);

        [LoggerMessage(EventId = 80092, Level = LogLevel.Information,
            Message = "Replay completed for notification type '{MessageType}'. {Count} messages replayed.")]
        public static partial void ReplayCompleted(this ILogger logger, string messageType, int count);

        [LoggerMessage(EventId = 80093, Level = LogLevel.Warning,
            Message = "Deferred message not found for type '{MessageType}'. Messages may have expired. Details: {Details}")]
        public static partial void DeferredMessageNotFound(this ILogger logger, string messageType, string details);

        [LoggerMessage(EventId = 80094, Level = LogLevel.Warning,
            Message = "{Count} deferred message(s) of type '{MessageType}' have expired and cannot be replayed.")]
        public static partial void DeferredMessagesExpired(this ILogger logger, string messageType, int count);

        [LoggerMessage(EventId = 80095, Level = LogLevel.Warning,
            Message = "{Count} deferred message(s) of type '{MessageType}' are approaching TTL expiry (oldest expires in {HoursUntilExpiry:F1} hours).")]
        public static partial void DeferredMessagesApproachingExpiry(this ILogger logger, string messageType, int count, double hoursUntilExpiry);

        [LoggerMessage(EventId = 80096, Level = LogLevel.Information,
            Message = "Replay skipped: global email suppression (SkipEmailNotifications) is still active.")]
        public static partial void ReplaySkippedGlobalSuppression(this ILogger logger);

        [LoggerMessage(EventId = 80097, Level = LogLevel.Warning,
            Message = "Failed to replay deferred message with sequence number {SequenceNumber} of type '{MessageType}': {Details}")]
        public static partial void DeferredMessageReplayFailed(this ILogger logger, long sequenceNumber, string messageType, string details);

        [LoggerMessage(EventId = 80098, Level = LogLevel.Warning,
            Message = "Error emitting observability metrics: {Details}")]
        public static partial void ObservabilityMetricsError(this ILogger logger, string details);
    }
}
