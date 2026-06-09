// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.GroupMembershipObtainer
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle (150000-150001) ──

        [LoggerMessage(EventId = 150000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 150001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction (150010-150019) ──

        [LoggerMessage(EventId = 150010, Level = LogLevel.Information,
            Message = "Processing message {MessageId}")]
        public static partial void ProcessingMessage(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 150011, Level = LogLevel.Information,
            Message = "InstanceId: {InstanceId} for job Id: {JobId}")]
        public static partial void OrchestrationInstanceStarted(this ILogger logger, string instanceId, Guid jobId);

        [LoggerMessage(EventId = 150012, Level = LogLevel.Information,
            Message = "Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period")]
        public static partial void DryRunSyncBackToIdle(this ILogger logger);

        // ── OrchestratorFunction (150020-150039) ──

        [LoggerMessage(EventId = 150020, Level = LogLevel.Warning,
            Message = "Found invalid value for CurrentPart or TotalParts")]
        public static partial void InvalidPartValues(this ILogger logger);

        [LoggerMessage(EventId = 150021, Level = LogLevel.Warning,
            Message = "Unable to get group id for job:{JobId}")]
        public static partial void UnableToGetGroupId(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 150022, Level = LogLevel.Information,
            Message = "Group Id for job:{JobId} is {GroupId}")]
        public static partial void GroupIdRetrieved(this ILogger logger, Guid jobId, Guid groupId);

        [LoggerMessage(EventId = 150023, Level = LogLevel.Warning,
            Message = "Source group id is not a valid, Part# {CurrentPart} {Query}. Marking job as {Status}.")]
        public static partial void InvalidSourceGroupId(this ILogger logger, int currentPart, string query, string status);

        [LoggerMessage(EventId = 150024, Level = LogLevel.Warning,
            Message = "Source query is not valid for job:{JobId}")]
        public static partial void InvalidSourceQuery(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 150025, Level = LogLevel.Warning,
            Message = "Rescheduling job at {StartDate} due to Graph API timeout.")]
        public static partial void ReschedulingJobDueToTimeout(this ILogger logger, DateTime? startDate);

        [LoggerMessage(EventId = 150026, Level = LogLevel.Error,
            Message = "Caught unexpected exception in Part# {CurrentPart}, marking sync job as errored.")]
        public static partial void OrchestratorUnexpectedException(this ILogger logger, Exception exception, int currentPart);

        // ── SubOrchestratorFunction (150040-150059) ──

        [LoggerMessage(EventId = 150040, Level = LogLevel.Information,
            Message = "Run transitive members query for group {GroupId}")]
        public static partial void RunTransitiveMembersQuery(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 150041, Level = LogLevel.Information,
            Message = "Run delta query for group {GroupId}")]
        public static partial void RunDeltaQuery(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 150042, Level = LogLevel.Warning,
            Message = "delta query failed for group {GroupId}: {ErrorMessage}")]
        public static partial void DeltaQueryFailed(this ILogger logger, Guid groupId, string errorMessage);

        [LoggerMessage(EventId = 150043, Level = LogLevel.Information,
            Message = "Run delta query using delta link for group {GroupId}")]
        public static partial void RunDeltaLinkQuery(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 150044, Level = LogLevel.Warning,
            Message = "delta query using delta link failed for group {GroupId}: {ErrorMessage}")]
        public static partial void DeltaLinkQueryFailed(this ILogger logger, Guid groupId, string errorMessage);

        [LoggerMessage(EventId = 150045, Level = LogLevel.Information,
            Message = "{GroupId} has {UserCount} users but cache {CacheCount} users. Running delta query...")]
        public static partial void CacheMismatchRunningDelta(this ILogger logger, Guid groupId, int userCount, int cacheCount);

        [LoggerMessage(EventId = 150046, Level = LogLevel.Error,
            Message = "Caught HttpRequestException, marking sync job status as transient error.")]
        public static partial void SubOrchestratorHttpException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 150047, Level = LogLevel.Error,
            Message = "Caught Exception, marking sync job status as error.")]
        public static partial void SubOrchestratorUnexpectedException(this ILogger logger, Exception exception);

        // ── GroupValidatorFunction (150060-150069) ──

        [LoggerMessage(EventId = 150060, Level = LogLevel.Information,
            Message = "Group with ID {ObjectId} exists.")]
        public static partial void GroupExists(this ILogger logger, Guid objectId);

        [LoggerMessage(EventId = 150061, Level = LogLevel.Warning,
            Message = "Group with ID {ObjectId} doesn't exist. Stopping sync and marking as {Status}.")]
        public static partial void GroupNotExists(this ILogger logger, Guid objectId, string status);

        [LoggerMessage(EventId = 150062, Level = LogLevel.Warning,
            Message = "Exceeded {MaxRetries} while trying to determine if a group exists. Stopping sync and marking as error.")]
        public static partial void GroupExistsRetriesExceeded(this ILogger logger, int maxRetries);

        [LoggerMessage(EventId = 150063, Level = LogLevel.Warning,
            Message = "Marking sync job status as transient error. Exception:\n{ErrorMessage}")]
        public static partial void GroupValidatorTransientError(this ILogger logger, string errorMessage);

        // ── GroupReaderFunction (150070-150079) ──

        [LoggerMessage(EventId = 150070, Level = LogLevel.Information,
            Message = "Getting destination group for Part# {CurrentPart}, with group id {GroupId}.")]
        public static partial void GettingDestinationGroup(this ILogger logger, int currentPart, Guid groupId);

        [LoggerMessage(EventId = 150071, Level = LogLevel.Information,
            Message = "Getting source group for Part# {CurrentPart} {Query} to be synced into the destination group {GroupId}.")]
        public static partial void GettingSourceGroup(this ILogger logger, int currentPart, string query, Guid groupId);

        // ── FileDownloaderFunction (150080-150089) ──

        [LoggerMessage(EventId = 150080, Level = LogLevel.Information,
            Message = "Downloading file {FilePath}")]
        public static partial void DownloadingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 150081, Level = LogLevel.Information,
            Message = "Downloaded file {FilePath}")]
        public static partial void DownloadedFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 150082, Level = LogLevel.Information,
            Message = "File {FilePath} does not exist")]
        public static partial void FileNotFound(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 150083, Level = LogLevel.Information,
            Message = "File {FilePath} is older than 6 days and 23 hours")]
        public static partial void FileExpired(this ILogger logger, string filePath);

        // ── FileDeleterFunction (150090-150099) ──

        [LoggerMessage(EventId = 150090, Level = LogLevel.Information,
            Message = "Deleting file {FilePath}")]
        public static partial void DeletingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 150091, Level = LogLevel.Information,
            Message = "Deleted file {FilePath}")]
        public static partial void DeletedFile(this ILogger logger, string filePath);

        // ── SchemaValidatorFunction (150100-150109) ──

        [LoggerMessage(EventId = 150100, Level = LogLevel.Warning,
            Message = "No json schemas have been loaded. Skipping schema validation.")]
        public static partial void NoSchemasLoaded(this ILogger logger);

        [LoggerMessage(EventId = 150101, Level = LogLevel.Information,
            Message = "Query not valid: {Errors}")]
        public static partial void SchemaValidationFailed(this ILogger logger, string errors);

        [LoggerMessage(EventId = 150102, Level = LogLevel.Warning,
            Message = "No GroupMembership schema has been loaded. Skipping schema validation.")]
        public static partial void NoGroupMembershipSchema(this ILogger logger);

        // ── LogNestedGroupsFunction (150110-150119) ──

        [LoggerMessage(EventId = 150110, Level = LogLevel.Information,
            Message = "Retrieved {GroupCount} group-type members for group {GroupId}. Group IDs: {GroupIds}")]
        public static partial void RetrievedNestedGroups(this ILogger logger, int groupCount, Guid groupId, string groupIds);

        [LoggerMessage(EventId = 150111, Level = LogLevel.Warning,
            Message = "Error retrieving group-type members for group {GroupId}: {ErrorMessage}")]
        public static partial void NestedGroupsRetrievalError(this ILogger logger, Exception exception, Guid groupId, string errorMessage);

        // ── QueueMessageSenderFunction (150120-150129) ──

        [LoggerMessage(EventId = 150120, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to membership aggregator")]
        public static partial void SentMessageToAggregator(this ILogger logger, string messageId);

        // ── UsersSenderFunction (150130-150139) ──

        [LoggerMessage(EventId = 150130, Level = LogLevel.Information,
            Message = "Successfully uploaded {UserCount} users from source groups {Query} to blob storage to be put into the destination group {GroupId}.")]
        public static partial void UsersUploadedToBlob(this ILogger logger, int userCount, string query, Guid groupId);

        // ── ProcessCachedAndDeltaUsersFunction (150140-150159) ──

        [LoggerMessage(EventId = 150140, Level = LogLevel.Information,
            Message = "Before delta link updates, cache for group {GroupId} has {CachedUserCount} users.")]
        public static partial void CacheBeforeDeltaUpdate(this ILogger logger, Guid groupId, int cachedUserCount);

        [LoggerMessage(EventId = 150141, Level = LogLevel.Information,
            Message = "No delta users to add found for the group {GroupId} in blob storage.")]
        public static partial void NoDeltaUsersToAdd(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 150142, Level = LogLevel.Information,
            Message = "No delta users to remove for the group {GroupId} found in blob storage.")]
        public static partial void NoDeltaUsersToRemove(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 150143, Level = LogLevel.Information,
            Message = "After delta link call for group {GroupId} - Added {AddCount} delta users, Removed {RemoveCount} delta users. Total users in cache {TotalCacheCount}.")]
        public static partial void DeltaLinkUpdateSummary(this ILogger logger, Guid groupId, int addCount, int removeCount, int totalCacheCount);

        [LoggerMessage(EventId = 150144, Level = LogLevel.Information,
            Message = "After delta link updates, number of users from group {GroupId} {ActualCount} and cache {CacheCount} are equal. Uploading membership, cache, and delta link files.")]
        public static partial void CacheMatchesUploading(this ILogger logger, Guid groupId, int actualCount, int cacheCount);

        [LoggerMessage(EventId = 150145, Level = LogLevel.Information,
            Message = "After delta link call, successfully uploaded {CachedUserCount} users + delta link {DeltaUrl} to cache for group {GroupId}.")]
        public static partial void DeltaLinkCacheUploaded(this ILogger logger, int cachedUserCount, string deltaUrl, Guid groupId);

        [LoggerMessage(EventId = 150146, Level = LogLevel.Information,
            Message = "After delta link updates for group {GroupId}. Cache mismatch: cached={CachedCount}, actual={ActualCount}. Running initial delta call.")]
        public static partial void CacheMismatchRunningInitialDelta(this ILogger logger, Guid groupId, int cachedCount, int actualCount);

        [LoggerMessage(EventId = 150147, Level = LogLevel.Information,
            Message = "{FunctionName} No cache file path provided for group {GroupId}.")]
        public static partial void NoCacheFilePath(this ILogger logger, string functionName, Guid groupId);

        [LoggerMessage(EventId = 150148, Level = LogLevel.Information,
            Message = "Cache write race condition for group {GroupId}: another concurrent writer already committed the cache blob. " +
                      "Both writers produce identical content for the same source group, so the committed cache is valid. Skipping.")]
        public static partial void CacheWriteRaceConditionSkipped(this ILogger logger, Guid groupId);

        // ── CacheUploaderFunction (150160-150169) ──

        [LoggerMessage(EventId = 150160, Level = LogLevel.Information,
            Message = "Cache upload race condition for group {GroupId}: another concurrent writer already committed the cache blob. " +
                      "Both writers produce identical content for the same source group, so the committed cache is valid. Skipping.")]
        public static partial void CacheUploadRaceConditionSkipped(this ILogger logger, Guid groupId);

        // ── SGMembershipCalculator (150200-150219) ──

        [LoggerMessage(EventId = 150200, Level = LogLevel.Warning,
            Message = "Got a transient exception. Retrying after {SleepDuration}. Max retries: {MaxRetries}.")]
        public static partial void TransientRetryException(this ILogger logger, Exception exception, TimeSpan sleepDuration, int maxRetries);

        [LoggerMessage(EventId = 150201, Level = LogLevel.Information,
            Message = "Reading users from the group with ID {ObjectId}.")]
        public static partial void ReadingUsersFromGroup(this ILogger logger, Guid objectId);

        [LoggerMessage(EventId = 150202, Level = LogLevel.Information,
            Message = "Read {MemberCount} users from group {ObjectId} to be synced into the destination group {TargetGroupId}.")]
        public static partial void ReadUsersFromGroup(this ILogger logger, int memberCount, Guid objectId, Guid targetGroupId);

        [LoggerMessage(EventId = 150203, Level = LogLevel.Information,
            Message = "After initial delta call, successfully uploaded deltaLink {DeltaLink} to cache for group {GroupId}.")]
        public static partial void DeltaLinkUploadedToCache(this ILogger logger, string deltaLink, Guid groupId);

        [LoggerMessage(EventId = 150204, Level = LogLevel.Information,
            Message = "After initial delta call, successfully uploaded {UserCount} users to cache for group {GroupId}.")]
        public static partial void CacheUploadedForGroup(this ILogger logger, int userCount, Guid groupId);

        [LoggerMessage(EventId = 150205, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentNotificationQueueMessage(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 150206, Level = LogLevel.Information,
            Message = "Destination name not found in database; attempting to retrieve from Graph")]
        public static partial void DestinationNameNotInDatabase(this ILogger logger);
    }
}
