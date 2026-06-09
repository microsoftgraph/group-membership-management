// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.TeamsChannelUpdater
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 190000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 190001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction ──

        [LoggerMessage(EventId = 190010, Level = LogLevel.Information,
            Message = "Calling {InstanceId}")]
        public static partial void CallingOrchestrator(this ILogger logger, string instanceId);

        // ── QueueMessageOrchestratorFunction ──

        [LoggerMessage(EventId = 190020, Level = LogLevel.Information,
            Message = "There are no more messages to process at this time.")]
        public static partial void NoMoreMessages(this ILogger logger);

        [LoggerMessage(EventId = 190021, Level = LogLevel.Information,
            Message = "Processing message for group {GroupId}")]
        public static partial void ProcessingMessage(this ILogger logger, Guid groupId);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 190030, Level = LogLevel.Information,
            Message = "Received membership and will sync the obtained {DistinctMemberCount} distinct members")]
        public static partial void ReceivedMembership(this ILogger logger, int distinctMemberCount);

        [LoggerMessage(EventId = 190031, Level = LogLevel.Information,
            Message = "Synchronization for {TargetGroupId} is now complete. {MembersToAdd} users have been added. {MembersToRemove} users have been removed.")]
        public static partial void SyncComplete(this ILogger logger, Guid targetGroupId, int membersToAdd, int membersToRemove);

        [LoggerMessage(EventId = 190032, Level = LogLevel.Information,
            Message = "{FunctionName} function completed at: {CompletionTime}")]
        public static partial void OrchestratorCompleted(this ILogger logger, string functionName, DateTimeOffset completionTime);

        [LoggerMessage(EventId = 190033, Level = LogLevel.Warning,
            Message = "SyncJob is null. Removing the message from the queue...")]
        public static partial void SyncJobIsNull(this ILogger logger);

        [LoggerMessage(EventId = 190034, Level = LogLevel.Error,
            Message = "Caught unexpected exception, marking sync job as errored.")]
        public static partial void UnexpectedExceptionCaught(this ILogger logger, Exception exception);

        // ── TeamsChannelUpdaterSubOrchestratorFunction ──

        [LoggerMessage(EventId = 190050, Level = LogLevel.Information,
            Message = "{FunctionName} function started with batch size {BatchSize}")]
        public static partial void SubOrchestratorStarted(this ILogger logger, string functionName, int batchSize);

        [LoggerMessage(EventId = 190051, Level = LogLevel.Information,
            Message = "{Action} {SuccessCount}/{TotalCount} users so far.")]
        public static partial void BatchProgress(this ILogger logger, string action, int successCount, int totalCount);

        [LoggerMessage(EventId = 190052, Level = LogLevel.Information,
            Message = "Retrying {RetryCount} users")]
        public static partial void RetryingUsers(this ILogger logger, int retryCount);

        [LoggerMessage(EventId = 190053, Level = LogLevel.Information,
            Message = "{Action} {SuccessCount} users in total, {UsersNotFoundCount} users not found, {UsersFailedCount} users failed.")]
        public static partial void SubOrchestratorSummary(this ILogger logger, string action, int successCount, int usersNotFoundCount, int usersFailedCount);

        // ── FileDownloaderFunction ──

        [LoggerMessage(EventId = 190070, Level = LogLevel.Information,
            Message = "Downloading file {FilePath}")]
        public static partial void DownloadingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 190071, Level = LogLevel.Information,
            Message = "Cache File {FilePath} was not found")]
        public static partial void CacheFileNotFound(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 190072, Level = LogLevel.Information,
            Message = "Downloaded file {FilePath}")]
        public static partial void DownloadedFile(this ILogger logger, string filePath);

        // ── TeamsChannelUpdaterService ──

        [LoggerMessage(EventId = 190100, Level = LogLevel.Information,
            Message = "Set job status to {Status}.")]
        public static partial void SettingJobStatus(this ILogger logger, string status);

        [LoggerMessage(EventId = 190101, Level = LogLevel.Information,
            Message = "{StatusMessage}")]
        public static partial void SyncStatusMessage(this ILogger logger, string statusMessage);

        [LoggerMessage(EventId = 190102, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentNotificationMessage(this ILogger logger, string messageId);
    }
}
