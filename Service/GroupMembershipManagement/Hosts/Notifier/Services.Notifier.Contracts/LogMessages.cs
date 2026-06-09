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
    }
}
