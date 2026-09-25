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

        // EventIds 250107-250111 retired with the whole-query rules; not reused.

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
            Message = "Per-Part Rule approved the submission. Approved parts — SqlMembership(manager-self): {SqlManagerSelfCount}, SqlMembership(assistant): {SqlAssistantCount}, GroupMembership(public): {GroupPublicCount}, GroupMembership(owner): {GroupOwnerCount}; total parts: {TotalParts}.")]
        public static partial void PerPartRuleApproved(this ILogger logger, int sqlManagerSelfCount, int sqlAssistantCount, int groupPublicCount, int groupOwnerCount, int totalParts);

        // EventId 250118 retired with the per-part setting reader; not reused.

        [LoggerMessage(EventId = 250119, Level = LogLevel.Error,
            Message = "Error retrieving requestor's EmployeeId from the users table.")]
        public static partial void EmployeeIdRetrievalError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250120, Level = LogLevel.Error,
            Message = "Error retrieving the most recent succeeded run id from Data Factory.")]
        public static partial void RunIdRetrievalError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 250121, Level = LogLevel.Error,
            Message = "Unhandled error during Per-Part Rule evaluation.")]
        public static partial void PerPartRuleCheckError(this ILogger logger, Exception exception);

        // Only the manager id is compared; filters, manager depth, and exclusionary flags are not evaluated.
        // That is safe because a manager-based source resolves only within the requestor's own reporting
        // chain, which a depth or filter can narrow but never widen.
        [LoggerMessage(EventId = 250123, Level = LogLevel.Information,
            Message = "SqlMembership part {PartIndex} approved: the manager id in the source matches the requestor's EmployeeId.")]
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

        // EventId 250130 retired: per-part is now the only rule; not reused.

        [LoggerMessage(EventId = 250131, Level = LogLevel.Information,
            Message = "Auto-approval paths resolved — group-based: {GroupBasedEnabled}, org-leader: {OrgLeaderEnabled}.")]
        public static partial void AutoApprovalModesResolved(this ILogger logger, bool groupBasedEnabled, bool orgLeaderEnabled);

        [LoggerMessage(EventId = 250132, Level = LogLevel.Information,
            Message = "Per-Part Rule declined the submission at {PartType} part {PartIndex} ({Source}): {Reason} — {Details}. Evaluated {PartsEvaluated} of {TotalParts} parts. Approved before the failure — SqlMembership(manager-self): {SqlManagerSelfCount}, SqlMembership(assistant): {SqlAssistantCount}, GroupMembership(public): {GroupPublicCount}, GroupMembership(owner): {GroupOwnerCount}.")]
        public static partial void PerPartRuleDeclined(this ILogger logger, int partIndex, string partType, string source, string reason, string details, int partsEvaluated, int totalParts, int sqlManagerSelfCount, int sqlAssistantCount, int groupPublicCount, int groupOwnerCount);

        // EmployeeId is an HR identifier, so its value is never logged — only whether it resolved.
        [LoggerMessage(EventId = 250133, Level = LogLevel.Information,
            Message = "SqlMembership part evaluation context: users table {UsersTable}, requestor EmployeeId {EmployeeIdStatus}.")]
        public static partial void SqlPartEvaluationContext(this ILogger logger, string usersTable, string employeeIdStatus);

        // Not a validation error: a query can be perfectly valid for GMM yet use a shape the rule does not vouch for.
        [LoggerMessage(EventId = 250134, Level = LogLevel.Information,
            Message = "Per-Part Rule declined the submission: the query has no source parts the rule can evaluate. Cause: {Cause}. Submission needs manual review.")]
        public static partial void PerPartQueryNotParsable(this ILogger logger, string cause);

        // Only the published assistant relationship is compared; filters, manager depth, and exclusionary
        // flags are not evaluated, for the same reason as the manager-self approval (250123).
        [LoggerMessage(EventId = 250135, Level = LogLevel.Information,
            Message = "SqlMembership part {PartIndex} approved: the requestor is the published assistant of the manager referenced in the source.")]
        public static partial void SqlPartApprovedByAssistant(this ILogger logger, int partIndex);

        [LoggerMessage(EventId = 250136, Level = LogLevel.Error,
            Message = "Error reading the published assistant for the manager referenced in SqlMembership part {PartIndex}. Part rejected; submission needs manual review.")]
        public static partial void AssistantLookupError(this ILogger logger, int partIndex, Exception exception);

        [LoggerMessage(EventId = 250137, Level = LogLevel.Error,
            Message = "Error resolving the directory identity of the manager referenced in SqlMembership part {PartIndex}. Part rejected; submission needs manual review.")]
        public static partial void ManagerIdentityResolveError(this ILogger logger, int partIndex, Exception exception);

        [LoggerMessage(EventId = 250138, Level = LogLevel.Error,
            Message = "Error retrieving the leader's-assistant auto-approval setting.")]
        public static partial void LeaderAssistantSettingRetrievalError(this ILogger logger, Exception exception);

        // Reported separately from 250131 because this setting is read only when the submission contains a SqlMembership part.
        [LoggerMessage(EventId = 250139, Level = LogLevel.Information,
            Message = "Auto-approval paths resolved — leader-assistant: {LeaderAssistantEnabled}.")]
        public static partial void LeaderAssistantPathResolved(this ILogger logger, bool leaderAssistantEnabled);

        [LoggerMessage(EventId = 250140, Level = LogLevel.Error,
            Message = "Error retrieving the sensitive-attribute screening setting.")]
        public static partial void SensitiveScreeningSettingRetrievalError(this ILogger logger, Exception exception);

        // Fail closed: without the list, no filter can be cleared.
        [LoggerMessage(EventId = 250122, Level = LogLevel.Error,
            Message = "Error reading the Sensitive attribute list. Filter-bearing SqlMembership parts cannot be screened and will be rejected.")]
        public static partial void SensitiveAttributeListReadError(this ILogger logger, Exception exception);

        // Attribute names are admin config, so safe to log. The filter never is: it can hold HR values.
        [LoggerMessage(EventId = 250141, Level = LogLevel.Information,
            Message = "SqlMembership part {PartIndex} rejected: its filter references the Sensitive attribute '{AttributeName}'.")]
        public static partial void SqlPartRejectedBySensitiveFilter(this ILogger logger, int partIndex, string attributeName);

        // Logged once per submission so screening-off is distinguishable from nothing-flagged.
        [LoggerMessage(EventId = 250142, Level = LogLevel.Information,
            Message = "Sensitive attribute screening resolved — enabled: {ScreeningEnabled}, attributes marked Sensitive: {SensitiveAttributeCount}.")]
        public static partial void SensitiveScreeningResolved(this ILogger logger, bool screeningEnabled, int sensitiveAttributeCount);
    }
}
