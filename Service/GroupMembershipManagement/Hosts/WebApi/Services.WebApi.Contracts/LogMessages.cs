// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using System;

namespace Hosts.WebApi
{
    public static partial class LogMessages
    {
        // ── Generic operation lifecycle (90000-90099) ──

        [LoggerMessage(EventId = 90000, Level = LogLevel.Information,
            Message = "{OperationName} started")]
        public static partial void OperationStarted(this ILogger logger, string operationName);

        [LoggerMessage(EventId = 90001, Level = LogLevel.Information,
            Message = "{OperationName} completed")]
        public static partial void OperationCompleted(this ILogger logger, string operationName);

        [LoggerMessage(EventId = 90002, Level = LogLevel.Error,
            Message = "{OperationName} failed")]
        public static partial void OperationFailed(this ILogger logger, string operationName, Exception exception);

        // ── RequestHandlerBase lifecycle (90010-90019) ──
        // Used by Services.Contracts.RequestHandlerBase<TReq,TRes> for every request.

        [LoggerMessage(EventId = 90010, Level = LogLevel.Information,
            Message = "Started execution of request {RequestType} ({InstanceId})")]
        public static partial void RequestStarted(this ILogger logger, string requestType, Guid instanceId);

        [LoggerMessage(EventId = 90011, Level = LogLevel.Information,
            Message = "Completed execution of request {RequestType} ({InstanceId})")]
        public static partial void RequestCompleted(this ILogger logger, string requestType, Guid instanceId);

        // ── GetSupportEmailHandler (91300-91349) ──

        [LoggerMessage(EventId = 91300, Level = LogLevel.Information,
            Message = "Retrieved secret '{SecretName}' from Key Vault")]
        public static partial void SecretRetrievedFromKeyVault(this ILogger logger, string secretName);

        [LoggerMessage(EventId = 91301, Level = LogLevel.Warning,
            Message = "Failed to retrieve secret '{SecretName}' from Key Vault")]
        public static partial void SecretRetrievalFailedFromKeyVault(this ILogger logger, string secretName, Exception exception);

        [LoggerMessage(EventId = 91302, Level = LogLevel.Error,
            Message = "Unexpected error while retrieving support email addresses")]
        public static partial void SupportEmailRetrievalFailed(this ILogger logger, Exception exception);

        // ── ResolveNotificationHandler (91350-91399) ──

        [LoggerMessage(EventId = 91350, Level = LogLevel.Information,
            Message = "ResolveNotificationHandler request: ThresholdNotificationId: {ThresholdNotificationId}, TargetOfficeGroupId: {TargetOfficeGroupId}")]
        public static partial void ResolveNotificationRequestReceived(this ILogger logger, Guid thresholdNotificationId, Guid? targetOfficeGroupId);

        [LoggerMessage(EventId = 91351, Level = LogLevel.Warning,
            Message = "Failed to retrieve group name for group {GroupId}")]
        public static partial void GroupNameRetrievalFailed(this ILogger logger, Guid groupId, Exception exception);

        [LoggerMessage(EventId = 91352, Level = LogLevel.Information,
            Message = "Resolved notification. Setting sync status to {NewStatus}")]
        public static partial void NotificationResolvedSyncStatusUpdated(this ILogger logger, string newStatus);

        // ── GetThresholdNotificationHandler (91400-91449) ──

        [LoggerMessage(EventId = 91400, Level = LogLevel.Error,
            Message = "Error getting threshold notification for SyncJobId {SyncJobId}")]
        public static partial void ThresholdNotificationRetrievalFailed(this ILogger logger, Guid syncJobId, Exception exception);

        // ── NotificationService (93000-93049) ──

        [LoggerMessage(EventId = 93000, Level = LogLevel.Warning,
            Message = "Failed to retrieve group name for Group ID {TargetGroupId} (RunId={RunId})")]
        public static partial void NotificationGroupNameRetrievalFailed(this ILogger logger, Guid? runId, Guid targetGroupId, Exception exception);

        [LoggerMessage(EventId = 93001, Level = LogLevel.Warning,
            Message = "Key conflict detected: {Key} already exists in messageContent and will not be overwritten (RunId={RunId})")]
        public static partial void NotificationMessageContentKeyConflict(this ILogger logger, Guid? runId, string key);

        [LoggerMessage(EventId = 93002, Level = LogLevel.Information,
            Message = "Sent notification message {MessageId} to service bus notifications queue for notification type {NotificationType} (RunId={RunId})")]
        public static partial void NotificationMessageSent(this ILogger logger, Guid? runId, string messageId, NotificationMessageType notificationType);

        [LoggerMessage(EventId = 93003, Level = LogLevel.Error,
            Message = "Failed to send notification for type {NotificationType} (RunId={RunId})")]
        public static partial void NotificationSendFailed(this ILogger logger, Guid? runId, NotificationMessageType notificationType, Exception exception);

        // ── PostOperationHandler (91450-91499) ──

        [LoggerMessage(EventId = 91450, Level = LogLevel.Information,
            Message = "Processing operation {Operation}.")]
        public static partial void OperationProcessing(this ILogger logger, Operations operation);

        [LoggerMessage(EventId = 91451, Level = LogLevel.Information,
            Message = "Operation {Operation}. Service is already {CurrentStatus}")]
        public static partial void OperationServiceAlreadyInStatus(this ILogger logger, Operations operation, ServiceStatuses currentStatus);

        [LoggerMessage(EventId = 91452, Level = LogLevel.Error,
            Message = "Error in PostOperationHandler with operation {Operation}")]
        public static partial void PostOperationHandlerFailed(this ILogger logger, Operations operation, Exception exception);

        // ── OperationsTaskQueue (93050-93099) ──

        [LoggerMessage(EventId = 93050, Level = LogLevel.Information,
            Message = "Dequeued operation {Operation}")]
        public static partial void OperationDequeued(this ILogger logger, Operations operation);

        [LoggerMessage(EventId = 93051, Level = LogLevel.Information,
            Message = "Queuing operation {Operation}")]
        public static partial void OperationQueueing(this ILogger logger, Operations operation);
    }
}
