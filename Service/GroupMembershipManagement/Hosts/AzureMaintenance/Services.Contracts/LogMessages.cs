// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.AzureMaintenance
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 50000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 50001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 50010, Level = LogLevel.Information,
            Message = "{FunctionName} function started at: {StartTime}")]
        public static partial void OrchestratorStarted(this ILogger logger, string functionName, DateTimeOffset startTime);

        [LoggerMessage(EventId = 50011, Level = LogLevel.Information,
            Message = "{FunctionName} function completed at: {CompletionTime}")]
        public static partial void OrchestratorCompleted(this ILogger logger, string functionName, DateTimeOffset completionTime);

        // ── AzureMaintenanceService ──

        [LoggerMessage(EventId = 50020, Level = LogLevel.Information,
            Message = "Number of jobs to be purged: {JobCount}")]
        public static partial void JobsToBePurged(this ILogger logger, int jobCount);

        [LoggerMessage(EventId = 50021, Level = LogLevel.Information,
            Message = "Number of purged jobs: {JobCount}")]
        public static partial void PurgedJobs(this ILogger logger, int jobCount);

        [LoggerMessage(EventId = 50022, Level = LogLevel.Information,
            Message = "Purging Job with GroupId: {GroupId}, GroupName: {GroupName} and Status: {Status}")]
        public static partial void PurgingJob(this ILogger logger, Guid groupId, string groupName, string status);

        [LoggerMessage(EventId = 50023, Level = LogLevel.Information,
            Message = "Number of jobs to be deleted from PurgedSyncJobs table: {JobCount}")]
        public static partial void JobsToBeDeleted(this ILogger logger, int jobCount);

        [LoggerMessage(EventId = 50024, Level = LogLevel.Information,
            Message = "Number of jobs deleted from SyncJobs table: {JobCount}")]
        public static partial void JobsDeleted(this ILogger logger, int jobCount);

        [LoggerMessage(EventId = 50025, Level = LogLevel.Information,
            Message = "Jobs needing warning as of {WarningDate}: {JobCount}")]
        public static partial void JobsNeedingWarning(this ILogger logger, DateTime warningDate, int jobCount);

        [LoggerMessage(EventId = 50026, Level = LogLevel.Information,
            Message = "Starting to purge job history older than {CutoffDate:yyyy-MM-dd}")]
        public static partial void StartingHistoryPurge(this ILogger logger, DateTime cutoffDate);

        [LoggerMessage(EventId = 50027, Level = LogLevel.Information,
            Message = "Purged {DeletedCount} job history records older than {RetentionDays} days")]
        public static partial void HistoryPurged(this ILogger logger, int deletedCount, int retentionDays);

        [LoggerMessage(EventId = 50028, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentNotificationMessage(this ILogger logger, string messageId);
    }
}
