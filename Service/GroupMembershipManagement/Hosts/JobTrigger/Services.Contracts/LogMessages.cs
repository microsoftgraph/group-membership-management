// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.JobTrigger
{
    public static partial class Log
    {
        // ── Orchestrator (1000-1009) ──

        [LoggerMessage(EventId = 1000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started at: {StartTime}")]
        public static partial void OrchestratorStarted(this ILogger logger, string functionName, DateTime startTime);

        [LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed at: {CompletedTime}")]
        public static partial void OrchestratorCompleted(this ILogger logger, string functionName, DateTime completedTime);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
            Message = "{FunctionName} number of jobs in the syncJobs List: {JobCount}")]
        public static partial void OrchestratorJobCount(this ILogger logger, string functionName, int jobCount);

        // ── SubOrchestrator (1010-1019) ──

        [LoggerMessage(EventId = 1010, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void SubOrchestratorStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 1011, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void SubOrchestratorCompleted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 1012, Level = LogLevel.Information,
            Message = "Job is stuck InProgress after retry, setting status to ErroredDueToStuckInProgress")]
        public static partial void JobStuckInProgress(this ILogger logger);

        [LoggerMessage(EventId = 1013, Level = LogLevel.Warning,
            Message = "Destination query is empty or missing required fields for job {JobId}")]
        public static partial void DestinationQueryEmpty(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 1014, Level = LogLevel.Warning,
            Message = "Group not found for job {JobId}")]
        public static partial void GroupNotFound(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 1015, Level = LogLevel.Warning,
            Message = "Channel not found for job {JobId}")]
        public static partial void ChannelNotFound(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 1016, Level = LogLevel.Warning,
            Message = "Destination query is not valid for job {JobId}")]
        public static partial void DestinationQueryNotValid(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 1017, Level = LogLevel.Warning,
            Message = "Source query is not valid for job {JobId}")]
        public static partial void SourceQueryNotValid(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 1018, Level = LogLevel.Warning,
            Message = "Source query is empty for job {JobId}")]
        public static partial void SourceQueryEmpty(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 1019, Level = LogLevel.Error,
            Message = "Caught unexpected exception in SubOrchestratorFunction, marking sync job as errored")]
        public static partial void SubOrchestratorException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1090, Level = LogLevel.Warning,
            Message = "Failed to retrieve latest job state for job {JobId} before sending to Service Bus. Using in-memory object as fallback.")]
        public static partial void FailedToRetrieveLatestJobState(this ILogger logger, Guid jobId);

        // ── Activity Functions (1020-1039) ──

        [LoggerMessage(EventId = 1020, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void ActivityFunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 1021, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void ActivityFunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction (1040-1041) ──

        [LoggerMessage(EventId = 1040, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void StarterFunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 1041, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void StarterFunctionCompleted(this ILogger logger, string functionName);

        // ── GetJobsFunction (1042-1043) ──

        [LoggerMessage(EventId = 1042, Level = LogLevel.Debug,
            Message = "{FunctionName} function started at: {StartTime}")]
        public static partial void GetJobsFunctionStarted(this ILogger logger, string functionName, DateTime startTime);

        [LoggerMessage(EventId = 1043, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed at: {CompletedTime}")]
        public static partial void GetJobsFunctionCompleted(this ILogger logger, string functionName, DateTime completedTime);

        // ── DestinationVerifierFunction (1044) ──

        [LoggerMessage(EventId = 1044, Level = LogLevel.Information,
            Message = "Linked services: {Services}")]
        public static partial void LinkedServices(this ILogger logger, string services);

        // ── SchemaValidatorFunction (1050-1055) ──

        [LoggerMessage(EventId = 1050, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void SchemaValidatorStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 1051, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void SchemaValidatorCompleted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 1052, Level = LogLevel.Information,
            Message = "No json schemas loaded, skipping validation")]
        public static partial void NoJsonSchemasLoaded(this ILogger logger);

        [LoggerMessage(EventId = 1053, Level = LogLevel.Warning,
            Message = "Schema is not valid for property {PropertyKey}")]
        public static partial void SchemaNotValid(this ILogger logger, string propertyKey);

        [LoggerMessage(EventId = 1054, Level = LogLevel.Error,
            Message = "Unable to parse json for property {PropertyName}")]
        public static partial void UnableToParseJson(this ILogger logger, Exception exception, string propertyName);

        [LoggerMessage(EventId = 1055, Level = LogLevel.Information,
            Message = "Skipping schema validation for property {PropertyKey}")]
        public static partial void SkippingSchemaValidation(this ILogger logger, string propertyKey);

        // ── JobTriggerService (1060-1069) ──

        [LoggerMessage(EventId = 1060, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentNotificationMessage(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 1061, Level = LogLevel.Information,
            Message = "Starting job.")]
        public static partial void StartingJob(this ILogger logger);

        [LoggerMessage(EventId = 1062, Level = LogLevel.Information,
            Message = "Restarting job stuck in InProgress.")]
        public static partial void RestartingStuckJob(this ILogger logger);

        [LoggerMessage(EventId = 1063, Level = LogLevel.Information,
            Message = "Checking: {CheckDescription} exists.")]
        public static partial void CheckingExists(this ILogger logger, string checkDescription);

        [LoggerMessage(EventId = 1064, Level = LogLevel.Information,
            Message = "Check {ResultMessage}: {CheckDescription} {ExistenceDescription}.")]
        public static partial void CheckResult(this ILogger logger, string resultMessage, string checkDescription, string existenceDescription);

        [LoggerMessage(EventId = 1065, Level = LogLevel.Information,
            Message = "Tracked telemetry event {EventName}")]
        public static partial void TrackedTelemetryEvent(this ILogger logger, string eventName);
    }
}
