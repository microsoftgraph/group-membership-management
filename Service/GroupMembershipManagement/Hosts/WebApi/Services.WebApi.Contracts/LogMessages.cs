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

        // ── OpenAIController (91100-91149) ──

        [LoggerMessage(EventId = 91100, Level = LogLevel.Information,
            Message = "GenerateTitles request failed: Parts list cannot be null or empty")]
        public static partial void GenerateTitlesPartsNullOrEmpty(this ILogger logger);

        [LoggerMessage(EventId = 91101, Level = LogLevel.Information,
            Message = "GenerateTitles request failed: {InvalidPartCount} parts have invalid data")]
        public static partial void GenerateTitlesInvalidParts(this ILogger logger, int invalidPartCount);

        [LoggerMessage(EventId = 91102, Level = LogLevel.Information,
            Message = "Calling OpenAI service for {PartsCount} parts")]
        public static partial void CallingOpenAIService(this ILogger logger, int partsCount);

        [LoggerMessage(EventId = 91103, Level = LogLevel.Information,
            Message = "OpenAI service returned response. Duration: {DurationMs} ms")]
        public static partial void OpenAIServiceResponseReceived(this ILogger logger, double durationMs);

        [LoggerMessage(EventId = 91104, Level = LogLevel.Warning,
            Message = "OpenAI service returned empty response")]
        public static partial void OpenAIEmptyResponse(this ILogger logger);

        [LoggerMessage(EventId = 91105, Level = LogLevel.Warning,
            Message = "OpenAI response could not be deserialized or was empty")]
        public static partial void OpenAIResponseDeserializationEmpty(this ILogger logger);

        [LoggerMessage(EventId = 91106, Level = LogLevel.Information,
            Message = "Successfully deserialized {TitleCount} titles from OpenAI response")]
        public static partial void OpenAITitlesDeserialized(this ILogger logger, int titleCount);

        [LoggerMessage(EventId = 91107, Level = LogLevel.Warning,
            Message = "OpenAI returned {ReturnedCount} titles but expected {ExpectedCount}")]
        public static partial void OpenAITitleCountMismatch(this ILogger logger, int returnedCount, int expectedCount);

        [LoggerMessage(EventId = 91108, Level = LogLevel.Warning,
            Message = "OpenAI response missing titles for {MissingCount} parts: {MissingPartIds}")]
        public static partial void OpenAIMissingTitles(this ILogger logger, int missingCount, string missingPartIds);

        [LoggerMessage(EventId = 91109, Level = LogLevel.Warning,
            Message = "OpenAI response contains {EmptyCount} empty titles")]
        public static partial void OpenAIEmptyTitles(this ILogger logger, int emptyCount);

        [LoggerMessage(EventId = 91110, Level = LogLevel.Information,
            Message = "GenerateTitles request completed successfully for {TitleCount} parts")]
        public static partial void GenerateTitlesSucceeded(this ILogger logger, int titleCount);

        [LoggerMessage(EventId = 91111, Level = LogLevel.Error,
            Message = "Failed to parse OpenAI response as JSON. Full response: {FullResponse}")]
        public static partial void OpenAIResponseJsonParseFailed(this ILogger logger, string fullResponse, Exception exception);

        [LoggerMessage(EventId = 91112, Level = LogLevel.Warning,
            Message = "GenerateTitles request failed with ArgumentException")]
        public static partial void GenerateTitlesArgumentException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 91113, Level = LogLevel.Error,
            Message = "GenerateTitles request failed with InvalidOperationException")]
        public static partial void GenerateTitlesInvalidOperation(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 91114, Level = LogLevel.Warning,
            Message = "GenerateTitles request failed due to OpenAI rate limiting (HTTP 429)")]
        public static partial void OpenAIRateLimited(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 91115, Level = LogLevel.Error,
            Message = "GenerateTitles request failed with Azure RequestFailedException (Status: {Status})")]
        public static partial void OpenAIRequestFailed(this ILogger logger, int status, Exception exception);

        [LoggerMessage(EventId = 91116, Level = LogLevel.Error,
            Message = "GenerateTitles request failed with unexpected error")]
        public static partial void GenerateTitlesUnexpectedError(this ILogger logger, Exception exception);

        // ── CopilotChatHandler (91500-91549) ──

        [LoggerMessage(EventId = 91500, Level = LogLevel.Information,
            Message = "CopilotChatHandler: Processing chat message")]
        public static partial void CopilotChatProcessing(this ILogger logger);

        [LoggerMessage(EventId = 91501, Level = LogLevel.Information,
            Message = "CopilotChatHandler: Successfully generated response (SourcePartIncluded: {SourcePartIncluded})")]
        public static partial void CopilotChatResponseGenerated(this ILogger logger, bool sourcePartIncluded);

        [LoggerMessage(EventId = 91502, Level = LogLevel.Warning,
            Message = "CopilotChatHandler: Request timed out")]
        public static partial void CopilotChatTimeout(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 91503, Level = LogLevel.Error,
            Message = "CopilotChatHandler: Error processing request")]
        public static partial void CopilotChatFailed(this ILogger logger, Exception exception);

        // ── CopilotService (93100-93149) ──

        [LoggerMessage(EventId = 93100, Level = LogLevel.Warning,
            Message = "Retry {RetryCount} after {DelaySeconds}s")]
        public static partial void CopilotChatRetryAttempt(this ILogger logger, int retryCount, double delaySeconds, Exception exception);

        [LoggerMessage(EventId = 93101, Level = LogLevel.Debug,
            Message = "Tool call iteration {Iteration}/{Max}: {Tools}")]
        public static partial void CopilotToolCallIteration(this ILogger logger, int iteration, int max, string tools);

        [LoggerMessage(EventId = 93102, Level = LogLevel.Debug,
            Message = "Chat completed with {ToolCalls} tool calls, {SourceParts} source parts")]
        public static partial void CopilotChatLoopCompleted(this ILogger logger, int toolCalls, int sourceParts);

        [LoggerMessage(EventId = 93103, Level = LogLevel.Warning,
            Message = "Unknown tool requested: {FunctionName}")]
        public static partial void CopilotUnknownToolRequested(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 93104, Level = LogLevel.Warning,
            Message = "HR DB services (ISqlMembershipRepository/IDataFactoryRepository) unavailable")]
        public static partial void CopilotHrDbServicesUnavailable(this ILogger logger);

        [LoggerMessage(EventId = 93105, Level = LogLevel.Warning,
            Message = "HR validation failed")]
        public static partial void CopilotHrValidationFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 93106, Level = LogLevel.Warning,
            Message = "Graph UPN search failed for {Email}")]
        public static partial void CopilotGraphUpnSearchFailed(this ILogger logger, string email, Exception exception);

        [LoggerMessage(EventId = 93107, Level = LogLevel.Warning,
            Message = "Graph mail search failed for {Email}")]
        public static partial void CopilotGraphMailSearchFailed(this ILogger logger, string email, Exception exception);

        [LoggerMessage(EventId = 93108, Level = LogLevel.Warning,
            Message = "JSON parse error in LLM response")]
        public static partial void CopilotLlmResponseJsonParseError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 93109, Level = LogLevel.Information,
            Message = "Plain-text fallback: extracted filter={Filter}, leader={Leader}")]
        public static partial void CopilotPlainTextFallbackExtracted(this ILogger logger, string filter, string leader);

        [LoggerMessage(EventId = 93110, Level = LogLevel.Error,
            Message = "Failed to fetch HR attributes")]
        public static partial void CopilotHrAttributesFetchFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 93111, Level = LogLevel.Warning,
            Message = "Failed to get ADF run ID")]
        public static partial void CopilotAdfRunIdFetchFailed(this ILogger logger, Exception exception);
    }
}
