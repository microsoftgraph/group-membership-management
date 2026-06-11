// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.SyncJobUpdater
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 130000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 130001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 130002, Level = LogLevel.Debug,
            Message = "{FunctionName} function started at: {StartTime}")]
        public static partial void FunctionStartedAt(this ILogger logger, string functionName, DateTimeOffset startTime);

        // ── StarterFunction ──

        [LoggerMessage(EventId = 130010, Level = LogLevel.Information,
            Message = "InstanceId: {InstanceId} for job Id: {JobId}")]
        public static partial void OrchestratorInstanceCreated(this ILogger logger, string instanceId, Guid jobId);

        // ── SyncJobUpdaterService ──

        [LoggerMessage(EventId = 130020, Level = LogLevel.Warning,
            Message = "Unable to find sync job with ID {JobId}")]
        public static partial void SyncJobNotFound(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 130021, Level = LogLevel.Information,
            Message = "Updated sync job {JobId} status to {NewStatus}")]
        public static partial void SyncJobStatusUpdated(this ILogger logger, Guid jobId, string newStatus);
    }
}
