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

        // ── OperationsBackgroundService (92000-92099) ──

        [LoggerMessage(EventId = 92000, Level = LogLevel.Information,
            Message = "Reset operation completed.")]
        public static partial void OperationsResetCompleted(this ILogger logger);

        [LoggerMessage(EventId = 92001, Level = LogLevel.Information,
            Message = "Stop operation completed.")]
        public static partial void OperationsStopCompleted(this ILogger logger);

        [LoggerMessage(EventId = 92002, Level = LogLevel.Information,
            Message = "Start operation completed.")]
        public static partial void OperationsStartCompleted(this ILogger logger);

        [LoggerMessage(EventId = 92003, Level = LogLevel.Error,
            Message = "Unexpected error in OperationsBackgroundService")]
        public static partial void OperationsLoopUnexpectedError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 92004, Level = LogLevel.Information,
            Message = "{EntityName} drain starting (startRemaining={StartRemaining}, expectedIterations={ExpectedIterations}, maxIterations={MaxIterations}).")]
        public static partial void DrainStarting(this ILogger logger, string entityName, string startRemaining, int expectedIterations, int maxIterations);

        [LoggerMessage(EventId = 92005, Level = LogLevel.Warning,
            Message = "{EntityName} transient receive error ({Reason}); retrying (iteration {Iteration}).")]
        public static partial void DrainTransientReceiveError(this ILogger logger, string entityName, string reason, int iteration);

        [LoggerMessage(EventId = 92006, Level = LogLevel.Information,
            Message = "{EntityName} drained after {Iteration} iterations. Total received: {TotalReceived}")]
        public static partial void DrainCompleted(this ILogger logger, string entityName, int iteration, long totalReceived);

        [LoggerMessage(EventId = 92007, Level = LogLevel.Information,
            Message = "{EntityName} runtime indicates {Remaining} messages remain after an empty batch; continuing...")]
        public static partial void DrainRuntimeIndicatesRemaining(this ILogger logger, string entityName, long remaining);

        [LoggerMessage(EventId = 92008, Level = LogLevel.Warning,
            Message = "WARNING: {EntityName} stopping due to stagnation (remaining still {Remaining}) after {StagnantEmptyIterations} stagnant empty polls. Total received: {TotalReceived}")]
        public static partial void DrainStallDetected(this ILogger logger, string entityName, long remaining, int stagnantEmptyIterations, long totalReceived);

        [LoggerMessage(EventId = 92009, Level = LogLevel.Information,
            Message = "{EntityName} drained (runtime unavailable) after {Iteration} iterations. Total received: {TotalReceived}")]
        public static partial void DrainCompletedRuntimeUnavailable(this ILogger logger, string entityName, int iteration, long totalReceived);

        [LoggerMessage(EventId = 92010, Level = LogLevel.Information,
            Message = "{EntityName} drained (no runtime verification) after {Iteration} iterations. Total received: {TotalReceived}")]
        public static partial void DrainCompletedNoRuntimeVerification(this ILogger logger, string entityName, int iteration, long totalReceived);

        [LoggerMessage(EventId = 92011, Level = LogLevel.Information,
            Message = "{EntityName} progress: received {BatchCount} (total {TotalReceived}). Remaining (approx): {Remaining}")]
        public static partial void DrainProgressWithRemaining(this ILogger logger, string entityName, int batchCount, long totalReceived, string remaining);

        [LoggerMessage(EventId = 92012, Level = LogLevel.Information,
            Message = "{EntityName} progress: received {BatchCount} (total {TotalReceived}).")]
        public static partial void DrainProgress(this ILogger logger, string entityName, int batchCount, long totalReceived);

        [LoggerMessage(EventId = 92013, Level = LogLevel.Warning,
            Message = "WARNING: {EntityName} reached dynamic iteration cap {Iteration}/{MaxIterations}. Total received: {TotalReceived}. Remaining(est)={LastRemaining}")]
        public static partial void DrainIterationCapReached(this ILogger logger, string entityName, int iteration, int maxIterations, long totalReceived, string lastRemaining);

        [LoggerMessage(EventId = 92014, Level = LogLevel.Information,
            Message = "PURGE-SUMMARY entity=\"{EntityName}\" totalRemoved={TotalRemoved} iterations={Iterations}")]
        public static partial void PurgeSummary(this ILogger logger, string entityName, long totalRemoved, int iterations);

        [LoggerMessage(EventId = 92015, Level = LogLevel.Information,
            Message = "Clearing queue {QueueName}")]
        public static partial void ClearQueueStarted(this ILogger logger, string queueName);

        [LoggerMessage(EventId = 92016, Level = LogLevel.Information,
            Message = "Clearing queue {QueueName} completed")]
        public static partial void ClearQueueCompleted(this ILogger logger, string queueName);

        [LoggerMessage(EventId = 92017, Level = LogLevel.Information,
            Message = "Clearing topic {TopicName} subscription {SubscriptionName}")]
        public static partial void ClearTopicStarted(this ILogger logger, string topicName, string subscriptionName);

        [LoggerMessage(EventId = 92018, Level = LogLevel.Information,
            Message = "Clearing topic {TopicName} subscription {SubscriptionName} completed")]
        public static partial void ClearTopicCompleted(this ILogger logger, string topicName, string subscriptionName);

        [LoggerMessage(EventId = 92019, Level = LogLevel.Information,
            Message = "Processing deferred messages in {EntityName} (remaining: {Remaining})")]
        public static partial void DeferredMessagesProcessing(this ILogger logger, string entityName, long remaining);

        [LoggerMessage(EventId = 92020, Level = LogLevel.Warning,
            Message = "Transient error peeking {EntityName}: {Reason}. Retrying...")]
        public static partial void DeferredPeekTransientError(this ILogger logger, string entityName, string reason);

        [LoggerMessage(EventId = 92021, Level = LogLevel.Information,
            Message = "Deferred progress {EntityName}: cleared={TotalCleared}, failed={TotalFailed}, rate={RatePerSecond:F1}/sec")]
        public static partial void DeferredProgress(this ILogger logger, string entityName, long totalCleared, long totalFailed, double ratePerSecond);

        [LoggerMessage(EventId = 92022, Level = LogLevel.Information,
            Message = "DEFERRED-SUMMARY entity=\"{EntityName}\" cleared={TotalCleared} failed={TotalFailed} duration={DurationSeconds:F1}s")]
        public static partial void DeferredSummary(this ILogger logger, string entityName, long totalCleared, long totalFailed, double durationSeconds);

        [LoggerMessage(EventId = 92023, Level = LogLevel.Information,
            Message = "Some deferred messages not found in {EntityName} (may have expired)")]
        public static partial void DeferredMessagesNotFound(this ILogger logger, string entityName);

        [LoggerMessage(EventId = 92024, Level = LogLevel.Warning,
            Message = "Transient error processing deferred batch in {EntityName} (attempt {Attempt}/{MaxRetries}): {Reason}")]
        public static partial void DeferredBatchTransientError(this ILogger logger, string entityName, int attempt, int maxRetries, string reason);

        [LoggerMessage(EventId = 92025, Level = LogLevel.Error,
            Message = "Failed to process deferred batch in {EntityName} after {MaxRetries} attempts")]
        public static partial void DeferredBatchFailed(this ILogger logger, string entityName, int maxRetries, Exception exception);

        [LoggerMessage(EventId = 92026, Level = LogLevel.Information,
            Message = "Clearing (session-enabled) topic {TopicName} subscription {SubscriptionName}")]
        public static partial void ClearSessionTopicStarted(this ILogger logger, string topicName, string subscriptionName);

        [LoggerMessage(EventId = 92027, Level = LogLevel.Warning,
            Message = "Failed to accept next session for {TopicName}/{SubscriptionName}.")]
        public static partial void AcceptSessionFailed(this ILogger logger, string topicName, string subscriptionName, Exception exception);

        [LoggerMessage(EventId = 92028, Level = LogLevel.Information,
            Message = "Clearing session {SessionId} for {TopicName}/{SubscriptionName}")]
        public static partial void ClearSessionMessages(this ILogger logger, string sessionId, string topicName, string subscriptionName);

        [LoggerMessage(EventId = 92029, Level = LogLevel.Warning,
            Message = "Error while clearing session {SessionId} for {TopicName}/{SubscriptionName}.")]
        public static partial void ClearSessionFailed(this ILogger logger, string sessionId, string topicName, string subscriptionName, Exception exception);

        [LoggerMessage(EventId = 92030, Level = LogLevel.Information,
            Message = "Clearing (session-enabled) topic {TopicName} subscription {SubscriptionName} completed. Sessions processed: {SessionsCleared}")]
        public static partial void ClearSessionTopicCompleted(this ILogger logger, string topicName, string subscriptionName, int sessionsCleared);

        [LoggerMessage(EventId = 92031, Level = LogLevel.Information,
            Message = "Clearing topics and their subscriptions...")]
        public static partial void ClearAllTopicsStarting(this ILogger logger);

        [LoggerMessage(EventId = 92032, Level = LogLevel.Information,
            Message = "Clearing function's internal tables and queues...")]
        public static partial void ClearInternalTablesAndQueuesStarting(this ILogger logger);

        [LoggerMessage(EventId = 92033, Level = LogLevel.Information,
            Message = "Deleted table {TableName} from account {StorageAccountName} used by {FunctionName}")]
        public static partial void InternalTableDeleted(this ILogger logger, string tableName, string storageAccountName, string functionName);

        [LoggerMessage(EventId = 92034, Level = LogLevel.Warning,
            Message = "Failed to delete table {TableName} from account {StorageAccountName} used by {FunctionName}.")]
        public static partial void InternalTableDeleteFailed(this ILogger logger, string tableName, string storageAccountName, string functionName, Exception exception);

        [LoggerMessage(EventId = 92035, Level = LogLevel.Information,
            Message = "Cleared queue {QueueName} from account {StorageAccountName} used by {FunctionName}")]
        public static partial void InternalQueueCleared(this ILogger logger, string queueName, string storageAccountName, string functionName);

        [LoggerMessage(EventId = 92036, Level = LogLevel.Warning,
            Message = "Failed to clear queue {QueueName} from account {StorageAccountName} used by {FunctionName}.")]
        public static partial void InternalQueueClearFailed(this ILogger logger, string queueName, string storageAccountName, string functionName, Exception exception);

        [LoggerMessage(EventId = 92037, Level = LogLevel.Information,
            Message = "ResetJobsInProgressAsync: Starting bulk reset of InProgress jobs to Idle.")]
        public static partial void ResetJobsInProgressStarting(this ILogger logger);

        [LoggerMessage(EventId = 92038, Level = LogLevel.Information,
            Message = "ResetJobsInProgressAsync: Successfully reset {Count} InProgress jobs to Idle.")]
        public static partial void ResetJobsInProgressCompleted(this ILogger logger, int count);

        [LoggerMessage(EventId = 92039, Level = LogLevel.Warning,
            Message = "ResetJobsInProgressAsync failed or timed out. Proceeding with operation.")]
        public static partial void ResetJobsInProgressFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 92040, Level = LogLevel.Warning,
            Message = "Failed to call JobScheduler ({StatusCode}). Retrying... {Retry}")]
        public static partial void JobSchedulerCallRetry(this ILogger logger, System.Net.HttpStatusCode statusCode, int retry);

        [LoggerMessage(EventId = 92041, Level = LogLevel.Information,
            Message = "Calling JobScheduler...")]
        public static partial void JobSchedulerCalling(this ILogger logger);

        [LoggerMessage(EventId = 92042, Level = LogLevel.Information,
            Message = "JobScheduler response: {StatusCode}.\n{ResponseContent}")]
        public static partial void JobSchedulerResponse(this ILogger logger, System.Net.HttpStatusCode statusCode, string responseContent);

        [LoggerMessage(EventId = 92043, Level = LogLevel.Error,
            Message = "Failed to call JobScheduler.")]
        public static partial void JobSchedulerFailed(this ILogger logger, Exception exception);

        // ── Destinations + ServiceStatus (91200-91299) ──

        [LoggerMessage(EventId = 91200, Level = LogLevel.Warning,
            Message = "Unable to retrieve group endpoints")]
        public static partial void GroupEndpointsRetrievalFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 91201, Level = LogLevel.Warning,
            Message = "Unable to retrieve group owners for group {GroupId}")]
        public static partial void GroupOwnersRetrievalFailed(this ILogger logger, Guid groupId, Exception exception);

        [LoggerMessage(EventId = 91202, Level = LogLevel.Information,
            Message = "Retrieved {GroupMemberCount} group-type members for group {GroupId}. Group IDs: {GroupIds}")]
        public static partial void GroupMembersRetrieved(this ILogger logger, int groupMemberCount, Guid groupId, string groupIds);

        [LoggerMessage(EventId = 91203, Level = LogLevel.Warning,
            Message = "Unable to retrieve group-type members for group {GroupId}")]
        public static partial void GroupMembersRetrievalFailed(this ILogger logger, Guid groupId, Exception exception);

        [LoggerMessage(EventId = 91204, Level = LogLevel.Warning,
            Message = "Unable to retrieve group endpoints for group details")]
        public static partial void GroupDetailsEndpointsRetrievalFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 91205, Level = LogLevel.Information,
            Message = "Retrieving service status.")]
        public static partial void ServiceStatusRetrieving(this ILogger logger);

        [LoggerMessage(EventId = 91206, Level = LogLevel.Information,
            Message = "Current service status is {Status}.")]
        public static partial void ServiceStatusCurrent(this ILogger logger, string status);

        // ── ResourceManagerService (93200-93299) ──

        [LoggerMessage(EventId = 93200, Level = LogLevel.Information,
            Message = "Stopping GMM...")]
        public static partial void GmmStopping(this ILogger logger);

        [LoggerMessage(EventId = 93201, Level = LogLevel.Information,
            Message = "Stopped {WebsiteName}")]
        public static partial void WebsiteStopped(this ILogger logger, string websiteName);

        [LoggerMessage(EventId = 93202, Level = LogLevel.Information,
            Message = "Starting GMM...")]
        public static partial void GmmStarting(this ILogger logger);

        [LoggerMessage(EventId = 93203, Level = LogLevel.Information,
            Message = "Started {WebsiteName}")]
        public static partial void WebsiteStarted(this ILogger logger, string websiteName);

        [LoggerMessage(EventId = 93204, Level = LogLevel.Information,
            Message = "Starting {WebsiteName}...")]
        public static partial void WebsiteStartingNamed(this ILogger logger, string websiteName);

        [LoggerMessage(EventId = 93205, Level = LogLevel.Information,
            Message = "{WebsiteName} responded with code: {StatusCode}.")]
        public static partial void WebsiteStartResponseStatus(this ILogger logger, string websiteName, int statusCode);

        [LoggerMessage(EventId = 93206, Level = LogLevel.Information,
            Message = "{WebsiteName} is now {State}.")]
        public static partial void WebsiteStateAfterStart(this ILogger logger, string websiteName, string state);

        [LoggerMessage(EventId = 93207, Level = LogLevel.Warning,
            Message = "{WebsiteName} was not found.")]
        public static partial void WebsiteNotFound(this ILogger logger, string websiteName);

        [LoggerMessage(EventId = 93208, Level = LogLevel.Error,
            Message = "Failed to start {WebsiteName}.")]
        public static partial void WebsiteStartFailed(this ILogger logger, string websiteName, Exception exception);

        [LoggerMessage(EventId = 93209, Level = LogLevel.Information,
            Message = "Getting storage account names...")]
        public static partial void GettingStorageAccountNames(this ILogger logger);

        [LoggerMessage(EventId = 93210, Level = LogLevel.Information,
            Message = "Getting websites...")]
        public static partial void GettingWebsites(this ILogger logger);
    }
}
