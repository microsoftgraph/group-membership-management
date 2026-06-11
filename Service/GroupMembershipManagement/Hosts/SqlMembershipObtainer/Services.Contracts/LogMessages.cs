// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.SqlMembershipObtainer
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 160000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 160001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction ──

        [LoggerMessage(EventId = 160010, Level = LogLevel.Information,
            Message = "Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period")]
        public static partial void DryRunIdleStatus(this ILogger logger);

        [LoggerMessage(EventId = 160011, Level = LogLevel.Information,
            Message = "InstanceId: {InstanceId} for job Id: {JobId}")]
        public static partial void OrchestrationInstanceStarted(this ILogger logger, string instanceId, Guid jobId);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 160020, Level = LogLevel.Warning,
            Message = "The job Id:{JobId} Part#{CurrentPart} does not have a valid query!")]
        public static partial void QueryNotValid(this ILogger logger, Guid jobId, int currentPart);

        [LoggerMessage(EventId = 160021, Level = LogLevel.Warning,
            Message = "Source query is not valid for job:{JobId}")]
        public static partial void SourceQueryNotValid(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 160022, Level = LogLevel.Warning,
            Message = "Unable to get group id for job:{JobId}")]
        public static partial void UnableToGetGroupId(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 160023, Level = LogLevel.Information,
            Message = "Group Id for job:{JobId} is {GroupId}")]
        public static partial void GroupIdRetrieved(this ILogger logger, Guid jobId, Guid groupId);

        [LoggerMessage(EventId = 160024, Level = LogLevel.Warning,
            Message = "Membership file path is not valid, marking sync job as {Status}.")]
        public static partial void FilePathNotValid(this ILogger logger, string status);

        [LoggerMessage(EventId = 160025, Level = LogLevel.Information,
            Message = "Rescheduling job at {StartDate} due to {Reason} exception")]
        public static partial void ReschedulingJob(this ILogger logger, DateTime? startDate, string reason);

        [LoggerMessage(EventId = 160026, Level = LogLevel.Error,
            Message = "Caught SqlException, marking sync job as errored.")]
        public static partial void SqlExceptionCaught(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 160027, Level = LogLevel.Error,
            Message = "{FunctionName} failed\n {ErrorMessage}")]
        public static partial void OrchestratorFailed(this ILogger logger, string functionName, string errorMessage);

        // ── OrganizationProcessorFunction ──

        [LoggerMessage(EventId = 160030, Level = LogLevel.Warning,
            Message = "Table does not exist")]
        public static partial void TableDoesNotExist(this ILogger logger);

        // ── SchemaValidatorFunction ──

        [LoggerMessage(EventId = 160040, Level = LogLevel.Warning,
            Message = "No json schemas have been loaded. Skipping schema validation.")]
        public static partial void NoJsonSchemasLoaded(this ILogger logger);

        [LoggerMessage(EventId = 160041, Level = LogLevel.Warning,
            Message = "Query not valid: {Errors}")]
        public static partial void SchemaQueryNotValid(this ILogger logger, string errors);

        [LoggerMessage(EventId = 160042, Level = LogLevel.Warning,
            Message = "No SqlMembership schema has been loaded. Skipping schema validation.")]
        public static partial void NoSqlMembershipSchemaLoaded(this ILogger logger);

        // ── QueueMessageSenderFunction ──

        [LoggerMessage(EventId = 160050, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to membership aggregator")]
        public static partial void SentMessageToAggregator(this ILogger logger, string messageId);

        // ── SqlMembershipObtainerService ──

        [LoggerMessage(EventId = 160060, Level = LogLevel.Information,
            Message = "Retrieved a total of {RecordCount} records from {TableName} table")]
        public static partial void RecordsRetrieved(this ILogger logger, int recordCount, string tableName);

        [LoggerMessage(EventId = 160061, Level = LogLevel.Information,
            Message = "Beginning to filter entities from {TableName} table")]
        public static partial void BeginningFilterEntities(this ILogger logger, string tableName);

        [LoggerMessage(EventId = 160062, Level = LogLevel.Information,
            Message = "Time to upload file: {Duration}")]
        public static partial void FileUploadDuration(this ILogger logger, TimeSpan duration);

        [LoggerMessage(EventId = 160063, Level = LogLevel.Information,
            Message = "Sent {MemberCount} members for group {GroupId}")]
        public static partial void MembersSentForGroup(this ILogger logger, int memberCount, Guid groupId);

        [LoggerMessage(EventId = 160064, Level = LogLevel.Information,
            Message = "SqlMembershipObtainer service completed at: {CompletionTime}")]
        public static partial void ServiceCompleted(this ILogger logger, DateTime completionTime);

        [LoggerMessage(EventId = 160065, Level = LogLevel.Information,
            Message = "{TableName} exists")]
        public static partial void TableNameExists(this ILogger logger, string tableName);

        [LoggerMessage(EventId = 160066, Level = LogLevel.Information,
            Message = "{TableName} does not exist")]
        public static partial void TableNameDoesNotExist(this ILogger logger, string tableName);

        [LoggerMessage(EventId = 160067, Level = LogLevel.Information,
            Message = "Updating status of job {JobId} to Idle.")]
        public static partial void UpdatingJobStatusToIdle(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 160068, Level = LogLevel.Information,
            Message = "Setting status of job {JobId} to {Status}.")]
        public static partial void SettingJobStatus(this ILogger logger, Guid jobId, string status);

        // ── DataFactoryService ──

        [LoggerMessage(EventId = 160070, Level = LogLevel.Information,
            Message = "Getting most recent ADF run id.")]
        public static partial void GettingAdfRunId(this ILogger logger);

        [LoggerMessage(EventId = 160071, Level = LogLevel.Warning,
            Message = "No SqlMembershipObtainer pipeline run has been found")]
        public static partial void NoPipelineRunFound(this ILogger logger);
    }
}
