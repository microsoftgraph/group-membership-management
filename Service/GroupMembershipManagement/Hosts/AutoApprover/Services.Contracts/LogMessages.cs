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

        [LoggerMessage(EventId = 250001, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 250002, Level = LogLevel.Information,
            Message = "AutoApprover message received. MessageId: {MessageId}. BodyLength: {BodyLength}")]
        public static partial void MessageReceived(this ILogger logger, string messageId, int bodyLength);

        [LoggerMessage(EventId = 250003, Level = LogLevel.Debug,
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
            Message = "Failed to persist auto-approval for sync job {SyncJobId}. Reverting status to PendingAutoApproval for retry.")]
        public static partial void AutoApprovalPersistenceFailed(this ILogger logger, Guid syncJobId, Exception exception);

        [LoggerMessage(EventId = 250115, Level = LogLevel.Error,
            Message = "Failed to revert sync job {SyncJobId} back to PendingAutoApproval after auto-approval persistence failure. Job may be left in Idle status without an audit record.")]
        public static partial void AutoApprovalRevertFailed(this ILogger logger, Guid syncJobId, Exception exception);

        // ── Per-Part Rule ──

        [LoggerMessage(EventId = 250116, Level = LogLevel.Information,
            Message = "Per-Part Rule approved the submission. Approved parts — SqlMembership(manager-self): {SqlApprovedCount}, GroupMembership(public): {GroupPublicCount}, GroupMembership(owner): {GroupOwnerCount}; total parts: {TotalParts}.")]
        public static partial void PerPartRuleApproved(this ILogger logger, int sqlApprovedCount, int groupPublicCount, int groupOwnerCount, int totalParts);

        [LoggerMessage(EventId = 250118, Level = LogLevel.Error,
            Message = "Error retrieving Per-Part Rule setting {SettingKey}.")]
        public static partial void SettingRetrievalError(this ILogger logger, string settingKey, Exception exception);

        [LoggerMessage(EventId = 250119, Level = LogLevel.Error,
            Message = "Error retrieving requestor's EmployeeId from the users table.")]
        public static partial void EmployeeIdRetrievalError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250120, Level = LogLevel.Error,
            Message = "Error retrieving the most recent succeeded run id from Data Factory.")]
        public static partial void RunIdRetrievalError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250121, Level = LogLevel.Error,
            Message = "Unhandled error during Per-Part Rule evaluation.")]
        public static partial void PerPartRuleCheckError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250123, Level = LogLevel.Information,
            Message = "SqlMembership part {PartIndex} approved: the manager id in the source matches the requestor's EmployeeId. Only the manager id is compared — any filter, manager depth, or exclusionary flag on this part is not evaluated. That is safe because a manager-based source resolves only within the requestor's own reporting chain, which a depth or filter can narrow but never widen.")]
        public static partial void SqlPartApprovedByManagerSelf(this ILogger logger, int partIndex);

        [LoggerMessage(EventId = 250125, Level = LogLevel.Information,
            Message = "GroupMembership part {PartIndex} ({GroupId}) approved: group visibility is Public.")]
        public static partial void GroupPartApprovedByPublicVisibility(this ILogger logger, int partIndex, string groupId);

        [LoggerMessage(EventId = 250126, Level = LogLevel.Information,
            Message = "GroupMembership part {PartIndex} ({GroupId}) approved: requestor is an owner (visibility {Visibility}).")]
        public static partial void GroupPartApprovedByOwner(this ILogger logger, int partIndex, string groupId, string visibility);

        [LoggerMessage(EventId = 250127, Level = LogLevel.Information,
            Message = "{PartType} part {PartIndex} ({Source}) rejected: {Reason} — {Details}.")]
        public static partial void SourcePartRejected(this ILogger logger, int partIndex, string partType, string source, string reason, string details);

        [LoggerMessage(EventId = 250128, Level = LogLevel.Error,
            Message = "Error retrieving visibility for group part {PartIndex} ({GroupId}).")]
        public static partial void GroupVisibilityError(this ILogger logger, int partIndex, string groupId, Exception exception);

        [LoggerMessage(EventId = 250129, Level = LogLevel.Error,
            Message = "Error retrieving owners for group part {PartIndex} ({GroupId}).")]
        public static partial void GroupOwnersError(this ILogger logger, int partIndex, string groupId, Exception exception);

        [LoggerMessage(EventId = 250130, Level = LogLevel.Information,
            Message = "Per-Part Rule declined the submission and is authoritative; group-based (enabled: {GroupBasedEnabled}) and org-leader (enabled: {OrgLeaderEnabled}) evaluation was skipped.")]
        public static partial void PerPartRuleTookPrecedence(this ILogger logger, bool groupBasedEnabled, bool orgLeaderEnabled);

        [LoggerMessage(EventId = 250131, Level = LogLevel.Information,
            Message = "Auto-approval modes resolved — group-based: {GroupBasedEnabled}, org-leader: {OrgLeaderEnabled}, per-part: {PerPartEnabled}.")]
        public static partial void AutoApprovalModesResolved(this ILogger logger, bool groupBasedEnabled, bool orgLeaderEnabled, bool perPartEnabled);

        [LoggerMessage(EventId = 250132, Level = LogLevel.Information,
            Message = "Per-Part Rule declined the submission at {PartType} part {PartIndex} ({Source}): {Reason} — {Details}. Evaluated {PartsEvaluated} of {TotalParts} parts. Approved before the failure — SqlMembership(manager-self): {SqlApprovedCount}, GroupMembership(public): {GroupPublicCount}, GroupMembership(owner): {GroupOwnerCount}.")]
        public static partial void PerPartRuleDeclined(this ILogger logger, int partIndex, string partType, string source, string reason, string details, int partsEvaluated, int totalParts, int sqlApprovedCount, int groupPublicCount, int groupOwnerCount);

        // EmployeeId is an HR identifier, so its value is never logged — only whether it resolved.
        [LoggerMessage(EventId = 250133, Level = LogLevel.Information,
            Message = "SqlMembership part evaluation context: users table {UsersTable}, requestor EmployeeId {EmployeeIdStatus}.")]
        public static partial void SqlPartEvaluationContext(this ILogger logger, string usersTable, string employeeIdStatus);

        [LoggerMessage(EventId = 250134, Level = LogLevel.Information,
            Message = "Per-Part Rule declined the submission: the query has no source parts the rule can evaluate. Cause: {Cause}. This does not mean the query is invalid for GMM — a query can be perfectly valid yet use a shape the rule does not vouch for — so the submission needs manual review.")]
        public static partial void PerPartQueryNotParsable(this ILogger logger, string cause);
    }
}
