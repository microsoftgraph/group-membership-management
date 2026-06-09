// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.GraphUpdater
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle (20000-20001) ──

        [LoggerMessage(EventId = 20000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 20001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction (20010-20029) ──

        [LoggerMessage(EventId = 20010, Level = LogLevel.Information,
            Message = "Processing message {MessageId} with {AdditionsCount} additions and {RemovalsCount} removals.")]
        public static partial void ProcessingMessage(this ILogger logger, string messageId, int additionsCount, int removalsCount);

        [LoggerMessage(EventId = 20011, Level = LogLevel.Warning,
            Message = "Message {MessageId} ({SequenceNumber}) was already processed ended as {Status}")]
        public static partial void MessageAlreadyProcessed(this ILogger logger, string messageId, long sequenceNumber, string status);

        [LoggerMessage(EventId = 20012, Level = LogLevel.Error,
            Message = "Error processing Service Bus message: {ErrorMessage}")]
        public static partial void StarterServiceBusError(this ILogger logger, Exception exception, string errorMessage);

        [LoggerMessage(EventId = 20013, Level = LogLevel.Information,
            Message = "Calling {InstanceId}")]
        public static partial void CallingOrchestrator(this ILogger logger, string instanceId);

        // ── OrchestratorFunction (20030-20049) ──

        [LoggerMessage(EventId = 20030, Level = LogLevel.Warning,
            Message = "Unable to get group id for job:{JobId}")]
        public static partial void UnableToGetGroupId(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 20031, Level = LogLevel.Information,
            Message = "Group Id for job:{JobId} is {GroupId}")]
        public static partial void GroupIdRetrieved(this ILogger logger, Guid jobId, Guid groupId);

        [LoggerMessage(EventId = 20032, Level = LogLevel.Information,
            Message = "Received membership from StarterFunction and will sync the obtained {DistinctMemberCount} distinct members")]
        public static partial void ReceivedMembership(this ILogger logger, int distinctMemberCount);

        [LoggerMessage(EventId = 20033, Level = LogLevel.Warning,
            Message = "OrchestratorFunction function did not complete")]
        public static partial void OrchestratorDidNotComplete(this ILogger logger);

        [LoggerMessage(EventId = 20034, Level = LogLevel.Warning,
            Message = "Failing the job because there was an error since guest users cannot be added to this group")]
        public static partial void GuestUsersCannotBeAdded(this ILogger logger);

        [LoggerMessage(EventId = 20035, Level = LogLevel.Information,
            Message = "{UsersDataMessage}")]
        public static partial void UsersDataInfo(this ILogger logger, string usersDataMessage);

        [LoggerMessage(EventId = 20036, Level = LogLevel.Error,
            Message = "Caught HttpRequestException, marking sync job status as transient error.")]
        public static partial void OrchestratorHttpException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 20037, Level = LogLevel.Error,
            Message = "Caught MsalClientException, marking sync job status as transient error.")]
        public static partial void OrchestratorMsalException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 20038, Level = LogLevel.Error,
            Message = "Caught unexpected exception, marking sync job as errored.")]
        public static partial void OrchestratorUnexpectedException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 20039, Level = LogLevel.Warning,
            Message = "SyncJob is null. Removing the message from the queue...")]
        public static partial void SyncJobIsNull(this ILogger logger);

        // ── OrchestratorMultiLaneFunction (20050-20069) ──

        [LoggerMessage(EventId = 20050, Level = LogLevel.Information,
            Message = "Received membership message {MessageIndex}/{TotalMessageCount} from StarterFunction and will sync the obtained {DistinctMemberCount} distinct members")]
        public static partial void ReceivedMembershipMultiLane(this ILogger logger, int messageIndex, int totalMessageCount, int distinctMemberCount);

        [LoggerMessage(EventId = 20051, Level = LogLevel.Information,
            Message = "Sync job status is {Status}. Skipping additional messages if any.")]
        public static partial void SyncJobStatusSkipping(this ILogger logger, string status);

        [LoggerMessage(EventId = 20052, Level = LogLevel.Warning,
            Message = "OrchestratorMultiLaneFunction function did not complete")]
        public static partial void MultiLaneDidNotComplete(this ILogger logger);

        [LoggerMessage(EventId = 20053, Level = LogLevel.Information,
            Message = "Users added {AddedSoFar}/{TotalToAdd} so far. Users removed {RemovedSoFar}/{TotalToRemove} so far.")]
        public static partial void MultiLaneProgressUpdate(this ILogger logger, int addedSoFar, int totalToAdd, int removedSoFar, int totalToRemove);

        [LoggerMessage(EventId = 20054, Level = LogLevel.Warning,
            Message = "Not all messages were processed, only {Processed} out of {Total} were processed.")]
        public static partial void NotAllMessagesProcessed(this ILogger logger, int processed, int total);

        // ── QueueMessageOrchestratorFunction (20070-20079) ──

        [LoggerMessage(EventId = 20070, Level = LogLevel.Information,
            Message = "There are no more messages to process at this time.")]
        public static partial void NoMoreMessages(this ILogger logger);

        [LoggerMessage(EventId = 20071, Level = LogLevel.Information,
            Message = "Processing message for group {GroupId}")]
        public static partial void ProcessingMessageForGroup(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 20072, Level = LogLevel.Error,
            Message = "Unexpected exception: {ErrorMessage}")]
        public static partial void QueueOrchestratorException(this ILogger logger, Exception exception, string errorMessage);

        // ── GroupUpdaterSubOrchestratorFunction (20080-20089) ──

        [LoggerMessage(EventId = 20080, Level = LogLevel.Information,
            Message = "{FunctionName} function started with batch size {BatchSize}")]
        public static partial void SubOrchestratorStartedWithBatchSize(this ILogger logger, string functionName, int batchSize);

        [LoggerMessage(EventId = 20081, Level = LogLevel.Information,
            Message = "{Operation} {SuccessCount}/{TotalCount} users so far.")]
        public static partial void GroupUpdateProgress(this ILogger logger, string operation, int successCount, int totalCount);

        [LoggerMessage(EventId = 20082, Level = LogLevel.Information,
            Message = "{Operation} {SuccessCount} users.")]
        public static partial void GroupUpdateComplete(this ILogger logger, string operation, int successCount);

        // ── CacheUserUpdaterSubOrchestratorFunction (20090-20099) ──

        [LoggerMessage(EventId = 20090, Level = LogLevel.Error,
            Message = "File not found exception in CacheUserUpdaterSubOrchestrator: {ErrorMessage}")]
        public static partial void CacheUpdaterFileNotFound(this ILogger logger, string errorMessage);

        [LoggerMessage(EventId = 20091, Level = LogLevel.Error,
            Message = "Unexpected exception in CacheUserUpdaterSubOrchestrator. {ErrorMessage}")]
        public static partial void CacheUpdaterUnexpectedException(this ILogger logger, Exception exception, string errorMessage);

        // ── GroupValidatorFunction (20100-20109) ──

        [LoggerMessage(EventId = 20100, Level = LogLevel.Information,
            Message = "Group with ID {GroupId} exists.")]
        public static partial void GroupExists(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 20101, Level = LogLevel.Warning,
            Message = "Group with ID {GroupId} doesn't exist.")]
        public static partial void GroupNotExists(this ILogger logger, Guid groupId);

        // ── FileDownloaderFunction (20110-20119) ──

        [LoggerMessage(EventId = 20110, Level = LogLevel.Information,
            Message = "Downloading file {FilePath}")]
        public static partial void DownloadingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 20111, Level = LogLevel.Information,
            Message = "Downloaded file {FilePath}")]
        public static partial void DownloadedFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 20112, Level = LogLevel.Warning,
            Message = "Cache File {FilePath} was not found")]
        public static partial void CacheFileNotFound(this ILogger logger, string filePath);

        // ── CacheUpdaterFunction (20120-20129) ──

        [LoggerMessage(EventId = 20120, Level = LogLevel.Information,
            Message = "CacheUpdaterFunction {UserCount} users to remove from cache/{GroupId}")]
        public static partial void CacheUpdaterRemovingUsers(this ILogger logger, int userCount, Guid groupId);

        [LoggerMessage(EventId = 20121, Level = LogLevel.Information,
            Message = "CacheUpdaterFunction Earlier count in cache/{GroupId}: {CacheCount}")]
        public static partial void CacheUpdaterEarlierCount(this ILogger logger, Guid groupId, int cacheCount);

        [LoggerMessage(EventId = 20122, Level = LogLevel.Information,
            Message = "CacheUpdaterFunction {UserCount} newUsers to add to cache/{GroupId}")]
        public static partial void CacheUpdaterAddingUsers(this ILogger logger, int userCount, Guid groupId);

        [LoggerMessage(EventId = 20123, Level = LogLevel.Information,
            Message = "Successfully uploaded {UserCount} users from group {GroupId} to cache {FileName}.")]
        public static partial void CacheUploadSuccess(this ILogger logger, int userCount, Guid groupId, string fileName);

        // ── MessageSplitterCompletionSenderFunction (20130-20139) ──

        [LoggerMessage(EventId = 20130, Level = LogLevel.Information,
            Message = "Completion message for RunId {RunId} has been sent.")]
        public static partial void CompletionMessageSent(this ILogger logger, Guid runId);

        // ── MessageSplitterLeaseRenewSenderFunction (20140-20149) ──

        [LoggerMessage(EventId = 20140, Level = LogLevel.Information,
            Message = "MessageSplitterLeaseRenewSenderFunction sending lease renew")]
        public static partial void SendingLeaseRenew(this ILogger logger);

        // ── MessageReaderFunction (20150-20159) ──

        // Uses generic FunctionStarted/FunctionCompleted
        // Note: Original code logged "function started" twice (bug) — preserving start/completed pattern

        // ── GraphUpdaterService (20200-20219) ──

        [LoggerMessage(EventId = 20200, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentNotificationQueueMessage(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 20201, Level = LogLevel.Information,
            Message = "Set job status to {Status}.")]
        public static partial void JobStatusUpdated(this ILogger logger, string status);

        [LoggerMessage(EventId = 20202, Level = LogLevel.Information,
            Message = "Dry Run of a sync to {GroupId} is complete. Membership will not be updated.")]
        public static partial void DryRunComplete(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 20203, Level = LogLevel.Information,
            Message = "Syncing to {GroupId} done.")]
        public static partial void SyncComplete(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 20204, Level = LogLevel.Information,
            Message = "Adding {MemberCount} users to group {GroupId} complete in {ElapsedSeconds} seconds. {RatePerSecond} users added per second.")]
        public static partial void UsersAddedToGroup(this ILogger logger, int memberCount, Guid groupId, double elapsedSeconds, double ratePerSecond);

        [LoggerMessage(EventId = 20205, Level = LogLevel.Information,
            Message = "Removing {MemberCount} users from group {GroupId} complete in {ElapsedSeconds} seconds. {RatePerSecond} users removed per second.")]
        public static partial void UsersRemovedFromGroup(this ILogger logger, int memberCount, Guid groupId, double elapsedSeconds, double ratePerSecond);

        [LoggerMessage(EventId = 20206, Level = LogLevel.Warning,
            Message = "Heartbeat task threw non-cancellation exception in finally block; swallowed to allow completion signal emission")]
        public static partial void HeartbeatFinallyException(this ILogger logger, Exception exception);
    }
}
