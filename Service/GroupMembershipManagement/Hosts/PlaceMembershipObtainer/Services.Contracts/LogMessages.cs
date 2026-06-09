// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.PlaceMembershipObtainer
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 180000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 180001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── Orchestrator ──

        [LoggerMessage(EventId = 180002, Level = LogLevel.Information,
            Message = "Unable to get group id for job:{JobId}")]
        public static partial void UnableToGetGroupId(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 180003, Level = LogLevel.Information,
            Message = "Group Id for job:{JobId} is {GroupId}")]
        public static partial void GroupIdFound(this ILogger logger, Guid jobId, Guid groupId);

        [LoggerMessage(EventId = 180004, Level = LogLevel.Information,
            Message = "Target Group")]
        public static partial void TargetGroup(this ILogger logger);

        [LoggerMessage(EventId = 180005, Level = LogLevel.Information,
            Message = "Not PlaceMembership Type")]
        public static partial void NotPlaceMembershipType(this ILogger logger);

        [LoggerMessage(EventId = 180006, Level = LogLevel.Information,
            Message = "No url found in Part# {CurrentPart} {Query}. Marking job as errored.")]
        public static partial void NoUrlFoundInPart(this ILogger logger, int currentPart, string query);

        [LoggerMessage(EventId = 180007, Level = LogLevel.Information,
            Message = "Source query is not valid for job:{JobId}")]
        public static partial void SourceQueryNotValidForJob(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 180008, Level = LogLevel.Information,
            Message = "Found {DuplicateCount} duplicate user(s). Read {DistinctCount} users from source groups {Query} to be synced into the destination group {GroupId}.")]
        public static partial void FoundDuplicateUsers(this ILogger logger, int duplicateCount, int distinctCount, string query, Guid groupId);

        [LoggerMessage(EventId = 180009, Level = LogLevel.Information,
            Message = "Calling MembershipAggregator")]
        public static partial void CallingMembershipAggregator(this ILogger logger);

        [LoggerMessage(EventId = 180010, Level = LogLevel.Information,
            Message = "Membership file path is not valid, marking sync job as {Status}.")]
        public static partial void MembershipFilePathNotValid(this ILogger logger, string status);

        [LoggerMessage(EventId = 180011, Level = LogLevel.Information,
            Message = "Rescheduling job at {StartDate} due to Graph API timeout.")]
        public static partial void ReschedulingJobDueToTimeout(this ILogger logger, DateTime startDate);

        [LoggerMessage(EventId = 180012, Level = LogLevel.Error,
            Message = "Caught unexpected exception in Part# {CurrentPart}, marking sync job as errored.")]
        public static partial void UnexpectedExceptionInPart(this ILogger logger, int currentPart, Exception exception);

        // ── Starter ──

        [LoggerMessage(EventId = 180013, Level = LogLevel.Information,
            Message = "Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period")]
        public static partial void SettingStatusToIdle(this ILogger logger);

        [LoggerMessage(EventId = 180014, Level = LogLevel.Information,
            Message = "InstanceId: {InstanceId} for job Id: {JobId}")]
        public static partial void InstanceIdCreated(this ILogger logger, string instanceId, Guid jobId);

        // ── SubOrchestrator ──

        [LoggerMessage(EventId = 180015, Level = LogLevel.Information,
            Message = "Getting results from next page for url: {Url}")]
        public static partial void GettingResultsFromNextPage(this ILogger logger, string url);

        [LoggerMessage(EventId = 180016, Level = LogLevel.Information,
            Message = "Url {Url} not supported")]
        public static partial void UrlNotSupported(this ILogger logger, string url);

        [LoggerMessage(EventId = 180017, Level = LogLevel.Information,
            Message = "Read {UserCount} users")]
        public static partial void ReadUsersCount(this ILogger logger, int userCount);

        // ── SchemaValidatorFunction ──

        [LoggerMessage(EventId = 180018, Level = LogLevel.Information,
            Message = "No json schemas have been loaded. Skipping schema validation.")]
        public static partial void NoJsonSchemasLoaded(this ILogger logger);

        [LoggerMessage(EventId = 180019, Level = LogLevel.Debug,
            Message = "Query not valid: {Errors}")]
        public static partial void QueryNotValid(this ILogger logger, string errors);

        [LoggerMessage(EventId = 180020, Level = LogLevel.Information,
            Message = "No PlaceMembership schema has been loaded. Skipping schema validation.")]
        public static partial void NoPlaceMembershipSchemaLoaded(this ILogger logger);

        // ── UsersSenderFunction ──

        [LoggerMessage(EventId = 180021, Level = LogLevel.Information,
            Message = "Successfully uploaded {UserCount} users from source groups {Query} to blob storage to be put into the destination group {GroupId}.")]
        public static partial void SuccessfullyUploadedUsers(this ILogger logger, int userCount, string query, Guid groupId);

        // ── QueueMessageSenderFunction ──

        [LoggerMessage(EventId = 180022, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to membership aggregator")]
        public static partial void SentMessageToMembershipAggregator(this ILogger logger, string messageId);
    }
}
