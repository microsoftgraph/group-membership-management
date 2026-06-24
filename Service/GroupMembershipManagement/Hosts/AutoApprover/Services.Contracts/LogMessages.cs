// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.AutoApprover
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 250000, Level = LogLevel.Information,
            Message = "AutoApprover is disabled. Skipping message processing.")]
        public static partial void AutoApproverDisabled(this ILogger logger);

        [LoggerMessage(EventId = 250001, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 250002, Level = LogLevel.Information,
            Message = "AutoApprover message received. MessageId: {MessageId}. BodyLength: {BodyLength}")]
        public static partial void MessageReceived(this ILogger logger, string messageId, int bodyLength);

        [LoggerMessage(EventId = 250003, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 250004, Level = LogLevel.Warning,
            Message = "AutoApprover message deserialized to null.")]
        public static partial void MessageDeserializedToNull(this ILogger logger);

        [LoggerMessage(EventId = 250005, Level = LogLevel.Error,
            Message = "AutoApprover message deserialization failed: {ErrorMessage}")]
        public static partial void MessageDeserializationFailed(this ILogger logger, string errorMessage);

        // ── AutoApproverService ──

        [LoggerMessage(EventId = 250100, Level = LogLevel.Warning,
            Message = "Sync job {SyncJobId} not found.")]
        public static partial void SyncJobNotFound(this ILogger logger, Guid syncJobId);

        [LoggerMessage(EventId = 250101, Level = LogLevel.Information,
            Message = "Sync job {SyncJobId} is pending configuration. Skipping auto-approval.")]
        public static partial void SyncJobPendingConfiguration(this ILogger logger, Guid syncJobId);

        [LoggerMessage(EventId = 250102, Level = LogLevel.Information,
            Message = "Sync job {SyncJobId} status is {Status}. Skipping auto-approval.")]
        public static partial void SyncJobStatusNotPendingReview(this ILogger logger, Guid syncJobId, string status);

        [LoggerMessage(EventId = 250103, Level = LogLevel.Warning,
            Message = "Invalid requestor object ID for sync job {SyncJobId}.")]
        public static partial void InvalidRequestorObjectId(this ILogger logger, Guid syncJobId);

        [LoggerMessage(EventId = 250104, Level = LogLevel.Information,
            Message = "Auto-approval not granted for sync job {SyncJobId}.")]
        public static partial void AutoApprovalNotGranted(this ILogger logger, Guid syncJobId);

        [LoggerMessage(EventId = 250105, Level = LogLevel.Information,
            Message = "Sync job {SyncJobId} auto-approved.")]
        public static partial void SyncJobAutoApproved(this ILogger logger, Guid syncJobId);

        [LoggerMessage(EventId = 250106, Level = LogLevel.Error,
            Message = "Error during auto-approval check.")]
        public static partial void AutoApprovalCheckError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250107, Level = LogLevel.Information,
            Message = "Auto-approval granted: All {GroupCount} source groups have acceptable visibility.")]
        public static partial void GroupVisibilityApprovalGranted(this ILogger logger, int groupCount);

        [LoggerMessage(EventId = 250108, Level = LogLevel.Error,
            Message = "Error during GroupMembership auto-approval check.")]
        public static partial void GroupMembershipApprovalCheckError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250109, Level = LogLevel.Information,
            Message = "Auto-approval granted: Single SqlMembership query with manager ID matching requestor's onPremisesImmutableId.")]
        public static partial void SqlMembershipApprovalGranted(this ILogger logger);

        [LoggerMessage(EventId = 250110, Level = LogLevel.Error,
            Message = "Error during SqlMembership auto-approval check.")]
        public static partial void SqlMembershipApprovalCheckError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250111, Level = LogLevel.Error,
            Message = "Error retrieving user onPremisesImmutableId.")]
        public static partial void UserImmutableIdRetrievalError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250112, Level = LogLevel.Error,
            Message = "Error retrieving auto-approval setting.")]
        public static partial void AutoApprovalSettingRetrievalError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250113, Level = LogLevel.Error,
            Message = "Error retrieving org leader auto-approval setting.")]
        public static partial void OrgLeaderSettingRetrievalError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250114, Level = LogLevel.Error,
            Message = "Failed to persist auto-approval for sync job {SyncJobId}. Reverting status to PendingReview for retry.")]
        public static partial void AutoApprovalPersistenceFailed(this ILogger logger, Guid syncJobId, Exception exception);

        [LoggerMessage(EventId = 250115, Level = LogLevel.Error,
            Message = "Failed to revert sync job {SyncJobId} back to PendingReview after auto-approval persistence failure. Job may be left in Idle status without an audit record.")]
        public static partial void AutoApprovalRevertFailed(this ILogger logger, Guid syncJobId, Exception exception);
    }
}
