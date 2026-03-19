// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.JobTrigger
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 10000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 10001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── Orchestrator ──

        [LoggerMessage(EventId = 10002, Level = LogLevel.Information,
            Message = "{FunctionName} number of jobs in the syncJobs List: {JobCount}")]
        public static partial void OrchestratorJobCount(this ILogger logger, string functionName, int jobCount);

        // ── SubOrchestrator ──

        [LoggerMessage(EventId = 10012, Level = LogLevel.Information,
            Message = "Job is stuck InProgress after retry, setting status to ErroredDueToStuckInProgress")]
        public static partial void JobStuckInProgress(this ILogger logger);

        [LoggerMessage(EventId = 10013, Level = LogLevel.Warning,
            Message = "Destination query is empty or missing required fields for job {JobId}")]
        public static partial void DestinationQueryEmpty(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 10014, Level = LogLevel.Warning,
            Message = "Group not found for job {JobId}")]
        public static partial void GroupNotFound(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 10015, Level = LogLevel.Warning,
            Message = "Channel not found for job {JobId}")]
        public static partial void ChannelNotFound(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 10016, Level = LogLevel.Warning,
            Message = "Destination query is not valid for job {JobId}")]
        public static partial void DestinationQueryNotValid(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 10017, Level = LogLevel.Warning,
            Message = "Source query is not valid for job {JobId}")]
        public static partial void SourceQueryNotValid(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 10018, Level = LogLevel.Warning,
            Message = "Source query is empty for job {JobId}")]
        public static partial void SourceQueryEmpty(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 10019, Level = LogLevel.Error,
            Message = "Caught unexpected exception in SubOrchestratorFunction, marking sync job as errored")]
        public static partial void SubOrchestratorException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 10090, Level = LogLevel.Warning,
            Message = "Failed to retrieve latest job state for job {JobId} before sending to Service Bus. Using in-memory object as fallback.")]
        public static partial void FailedToRetrieveLatestJobState(this ILogger logger, Guid jobId);

        // ── DestinationVerifierFunction ──

        [LoggerMessage(EventId = 10044, Level = LogLevel.Information,
            Message = "Linked services: {Services}")]
        public static partial void LinkedServices(this ILogger logger, string services);

        // ── SchemaValidatorFunction ──

        [LoggerMessage(EventId = 10052, Level = LogLevel.Information,
            Message = "No json schemas loaded, skipping validation")]
        public static partial void NoJsonSchemasLoaded(this ILogger logger);

        [LoggerMessage(EventId = 10053, Level = LogLevel.Warning,
            Message = "Schema is not valid for property {PropertyKey}")]
        public static partial void SchemaNotValid(this ILogger logger, string propertyKey);

        [LoggerMessage(EventId = 10054, Level = LogLevel.Error,
            Message = "Unable to parse json for property {PropertyName}")]
        public static partial void UnableToParseJson(this ILogger logger, string propertyName, Exception exception);

        [LoggerMessage(EventId = 10055, Level = LogLevel.Information,
            Message = "Skipping schema validation for property {PropertyKey}")]
        public static partial void SkippingSchemaValidation(this ILogger logger, string propertyKey);

        // ── JobTriggerService ──

        [LoggerMessage(EventId = 10060, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentNotificationMessage(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 10061, Level = LogLevel.Information,
            Message = "Starting job.")]
        public static partial void StartingJob(this ILogger logger);

        [LoggerMessage(EventId = 10062, Level = LogLevel.Information,
            Message = "Restarting job stuck in InProgress.")]
        public static partial void RestartingStuckJob(this ILogger logger);

        [LoggerMessage(EventId = 10063, Level = LogLevel.Information,
            Message = "Checking: {CheckDescription} exists.")]
        public static partial void CheckingExists(this ILogger logger, string checkDescription);

        [LoggerMessage(EventId = 10064, Level = LogLevel.Information,
            Message = "Check {ResultMessage}: {CheckDescription} {ExistenceDescription}.")]
        public static partial void CheckResult(this ILogger logger, string resultMessage, string checkDescription, string existenceDescription);

        [LoggerMessage(EventId = 10065, Level = LogLevel.Information,
            Message = "Tracked telemetry event {EventName}")]
        public static partial void TrackedTelemetryEvent(this ILogger logger, string eventName);
    }
}
