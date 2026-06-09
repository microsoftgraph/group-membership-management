// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.TeamsChannelMembershipObtainer
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 170000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 170001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction ──

        [LoggerMessage(EventId = 170010, Level = LogLevel.Information,
            Message = "TeamsChannelMembershipObtainer received a message. Query: {Query}")]
        public static partial void MessageReceived(this ILogger logger, string query);

        [LoggerMessage(EventId = 170011, Level = LogLevel.Information,
            Message = "InstanceId: {InstanceId} for job RowKey: {RowKey}")]
        public static partial void OrchestratorInstanceStarted(this ILogger logger, string instanceId, string rowKey);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 170020, Level = LogLevel.Information,
            Message = "{FunctionName} function started at: {StartTime}")]
        public static partial void OrchestratorStarted(this ILogger logger, string functionName, DateTimeOffset startTime);

        [LoggerMessage(EventId = 170021, Level = LogLevel.Information,
            Message = "{FunctionName} function completed at: {CompletionTime}")]
        public static partial void OrchestratorCompleted(this ILogger logger, string functionName, DateTimeOffset completionTime);

        [LoggerMessage(EventId = 170022, Level = LogLevel.Warning,
            Message = "Found invalid value for CurrentPart or TotalParts. Marked as Error.")]
        public static partial void InvalidPartValues(this ILogger logger);

        [LoggerMessage(EventId = 170023, Level = LogLevel.Warning,
            Message = "Teams Channel Destination did not validate. Marked as {Status}.")]
        public static partial void ChannelValidationFailed(this ILogger logger, string status);

        [LoggerMessage(EventId = 170024, Level = LogLevel.Error,
            Message = "Caught unexpected exception. Marking job as errored.")]
        public static partial void UnexpectedExceptionCaught(this ILogger logger, Exception exception);

        // ── FileUploaderFunction ──

        [LoggerMessage(EventId = 170030, Level = LogLevel.Information,
            Message = "Uploading {UserCount} users from Group: {GroupId} with Channel Id: {ChannelId} to blob storage.")]
        public static partial void UploadingUsers(this ILogger logger, int userCount, Guid groupId, string channelId);

        [LoggerMessage(EventId = 170031, Level = LogLevel.Information,
            Message = "Uploaded {UserCount} users from Group: {GroupId} with Channel Id: {ChannelId} to blob storage at {FilePath}.")]
        public static partial void UploadedUsers(this ILogger logger, int userCount, Guid groupId, string channelId, string filePath);

        // ── UserReaderFunction ──

        [LoggerMessage(EventId = 170040, Level = LogLevel.Information,
            Message = "Read {UserCount} users from Group: {GroupId} with Channel Id: {ChannelId}.")]
        public static partial void UsersRead(this ILogger logger, int userCount, Guid groupId, string channelId);

        // ── TeamsChannelMembershipObtainerService ──

        [LoggerMessage(EventId = 170100, Level = LogLevel.Warning,
            Message = "Unable to get destination details from TeamsChannels table.")]
        public static partial void DestinationDetailsNotFound(this ILogger logger);

        [LoggerMessage(EventId = 170101, Level = LogLevel.Information,
            Message = "Group {GroupId} and channel {ChannelId} is not a destination.")]
        public static partial void ChannelNotDestination(this ILogger logger, Guid groupId, string channelId);

        [LoggerMessage(EventId = 170102, Level = LogLevel.Information,
            Message = "Channel {ChannelId} from group {GroupId} is a standard channel.")]
        public static partial void StandardChannelDetected(this ILogger logger, string channelId, Guid groupId);

        [LoggerMessage(EventId = 170103, Level = LogLevel.Information,
            Message = "Channel {ChannelId} of group {GroupId} is of type {ChannelType}.")]
        public static partial void ChannelTypeDetected(this ILogger logger, string channelId, Guid groupId, string channelType);

        [LoggerMessage(EventId = 170104, Level = LogLevel.Information,
            Message = "Reading from group {GroupId} and channel {ChannelId}.")]
        public static partial void ReadingFromChannel(this ILogger logger, Guid groupId, string channelId);

        [LoggerMessage(EventId = 170105, Level = LogLevel.Information,
            Message = "Uploading {UserCount} users to {FileName}.")]
        public static partial void UploadingMembership(this ILogger logger, int userCount, string fileName);

        [LoggerMessage(EventId = 170106, Level = LogLevel.Information,
            Message = "Uploaded {UserCount} users to {FileName}.")]
        public static partial void UploadedMembership(this ILogger logger, int userCount, string fileName);

        [LoggerMessage(EventId = 170107, Level = LogLevel.Information,
            Message = "Sending message {MessageId} to membership aggregator.")]
        public static partial void SendingAggregatorMessage(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 170108, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to membership aggregator.")]
        public static partial void SentAggregatorMessage(this ILogger logger, string messageId);
    }
}
