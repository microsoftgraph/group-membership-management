// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.GroupOwnershipObtainer
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 140000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 140001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction ──

        [LoggerMessage(EventId = 140010, Level = LogLevel.Information,
            Message = "Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period")]
        public static partial void DryRunIdleStatus(this ILogger logger);

        [LoggerMessage(EventId = 140011, Level = LogLevel.Information,
            Message = "InstanceId: {InstanceId} for job Id: {JobId}")]
        public static partial void OrchestrationInstanceStarted(this ILogger logger, string instanceId, Guid jobId);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 140020, Level = LogLevel.Warning,
            Message = "Found invalid value for CurrentPart or TotalParts")]
        public static partial void InvalidCurrentOrTotalPart(this ILogger logger);

        [LoggerMessage(EventId = 140021, Level = LogLevel.Warning,
            Message = "The job Id:{JobId} Part#{CurrentPart} does not have a valid query!")]
        public static partial void JobQueryNotValid(this ILogger logger, Guid jobId, int currentPart);

        [LoggerMessage(EventId = 140022, Level = LogLevel.Warning,
            Message = "Source query is not valid for job:{JobId}")]
        public static partial void SourceQueryNotValid(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 140023, Level = LogLevel.Warning,
            Message = "Unable to get group id for job:{JobId}")]
        public static partial void UnableToGetGroupId(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 140024, Level = LogLevel.Information,
            Message = "Group Id for job:{JobId} is {GroupId}")]
        public static partial void GroupIdRetrieved(this ILogger logger, Guid jobId, Guid groupId);

        [LoggerMessage(EventId = 140025, Level = LogLevel.Information,
            Message = "There are no jobs matching the requested sources {Sources}")]
        public static partial void NoJobsMatchingRequestedSources(this ILogger logger, string sources);

        [LoggerMessage(EventId = 140026, Level = LogLevel.Information,
            Message = "{FunctionName} number of jobs in the syncJobs List: {JobCount}")]
        public static partial void OrchestratorJobCount(this ILogger logger, string functionName, int jobCount);

        [LoggerMessage(EventId = 140027, Level = LogLevel.Error,
            Message = "Caught unexpected exception in Part# {CurrentPart}, marking sync job as errored.")]
        public static partial void OrchestratorException(this ILogger logger, Exception exception, int currentPart);

        [LoggerMessage(EventId = 140028, Level = LogLevel.Information,
            Message = "Rescheduling job at {StartDate} due to Graph API timeout at Part#{CurrentPart}.")]
        public static partial void ReschedulingJobDueToTimeout(this ILogger logger, DateTime startDate, int currentPart);

        // ── SchemaValidatorFunction ──

        [LoggerMessage(EventId = 140040, Level = LogLevel.Warning,
            Message = "No json schemas have been loaded. Skipping schema validation.")]
        public static partial void NoJsonSchemasLoaded(this ILogger logger);

        [LoggerMessage(EventId = 140041, Level = LogLevel.Warning,
            Message = "Query not valid: {Errors}")]
        public static partial void SchemaQueryNotValid(this ILogger logger, string errors);

        [LoggerMessage(EventId = 140042, Level = LogLevel.Warning,
            Message = "No GroupOwnership schema has been loaded. Skipping schema validation.")]
        public static partial void NoGroupOwnershipSchemaLoaded(this ILogger logger);

        // ── GetJobsSegmentedFunction ──

        [LoggerMessage(EventId = 140050, Level = LogLevel.Information,
            Message = "{FunctionName} number of jobs about to be returned: {JobCount}")]
        public static partial void SegmentedJobsCount(this ILogger logger, string functionName, int jobCount);

        // ── QueueMessageSenderFunction ──

        [LoggerMessage(EventId = 140060, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to membership aggregator")]
        public static partial void SentMessageToAggregator(this ILogger logger, string messageId);

        // ── UsersSenderFunction ──

        [LoggerMessage(EventId = 140070, Level = LogLevel.Information,
            Message = "Successfully uploaded {UserCount} users from source groups {Query} to blob storage to be put into the destination group {GroupId}.")]
        public static partial void UsersUploaded(this ILogger logger, int userCount, string query, Guid groupId);

        // ── GroupOwnershipObtainerService ──

        [LoggerMessage(EventId = 140100, Level = LogLevel.Warning,
            Message = "Group {GroupId} does not exist")]
        public static partial void GroupDoesNotExist(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 140101, Level = LogLevel.Error,
            Message = "Unable to determine job type for group {GroupId}")]
        public static partial void UnableToDetermineJobType(this ILogger logger, Exception exception, Guid groupId);
    }
}
