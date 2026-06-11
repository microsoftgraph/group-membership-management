// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.MembershipAggregator
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 30000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 30001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction ──

        [LoggerMessage(EventId = 30010, Level = LogLevel.Information,
            Message = "Processing message {MessageId}")]
        public static partial void ProcessingMessage(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 30011, Level = LogLevel.Information,
            Message = "InstanceId: {InstanceId}")]
        public static partial void OrchestrationInstanceStarted(this ILogger logger, string instanceId);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 30020, Level = LogLevel.Warning,
            Message = "Unable to get group id for job:{JobId}")]
        public static partial void UnableToGetGroupId(this ILogger logger, Guid jobId);

        [LoggerMessage(EventId = 30021, Level = LogLevel.Information,
            Message = "Group Id for job:{JobId} is {GroupId}")]
        public static partial void GroupIdRetrieved(this ILogger logger, Guid jobId, Guid groupId);

        [LoggerMessage(EventId = 30022, Level = LogLevel.Warning,
            Message = "File not found exception in orchestrator")]
        public static partial void OrchestratorFileNotFound(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 30023, Level = LogLevel.Error,
            Message = "Unexpected exception in orchestrator")]
        public static partial void OrchestratorUnexpectedException(this ILogger logger, Exception exception);

        // ── MembershipSubOrchestratorFunction ──

        [LoggerMessage(EventId = 30030, Level = LogLevel.Warning,
            Message = "Failed to extract membership information for TargetOfficeGroupId {GroupId}: {ExtractionError}")]
        public static partial void MembershipExtractionFailed(this ILogger logger, Guid groupId, string extractionError);

        [LoggerMessage(EventId = 30031, Level = LogLevel.Warning,
            Message = "{MissingComponent} is missing for TargetOfficeGroupId {GroupId}. Marking job as 'Error'.")]
        public static partial void MissingMembershipComponent(this ILogger logger, string missingComponent, Guid groupId);

        [LoggerMessage(EventId = 30032, Level = LogLevel.Warning,
            Message = "Sources are empty for TargetOfficeGroupId {GroupId}. Empty destination is not allowed for this group. Marking job as 'MembershipDataNotFound'.")]
        public static partial void SourcesEmptyForGroup(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 30033, Level = LogLevel.Information,
            Message = "Reading membership data from blobs. SourceMembershipFilePath: {SourcePath}, DestinationMembershipFilePath: {DestinationPath}")]
        public static partial void ReadingMembershipFromBlobs(this ILogger logger, string sourcePath, string destinationPath);

        [LoggerMessage(EventId = 30034, Level = LogLevel.Warning,
            Message = "Aggregated membership upload error: {ErrorMessage}")]
        public static partial void AggregatedUploadError(this ILogger logger, string errorMessage);

        [LoggerMessage(EventId = 30035, Level = LogLevel.Information,
            Message = "Uploaded membership file {FilePath} with {MemberCount} unique members")]
        public static partial void UploadedMembershipFile(this ILogger logger, string filePath, int memberCount);

        [LoggerMessage(EventId = 30036, Level = LogLevel.Information,
            Message = "A Dry Run Synchronization for {GroupId} is now complete. {MembersToAddCount} users would have been added. {MembersToRemoveCount} users would have been removed.")]
        public static partial void DryRunSyncComplete(this ILogger logger, Guid groupId, int membersToAddCount, int membersToRemoveCount);

        [LoggerMessage(EventId = 30037, Level = LogLevel.Information,
            Message = "There are no membership changes for TargetOfficeGroupId {GroupId}.")]
        public static partial void NoMembershipChanges(this ILogger logger, Guid groupId);

        // ── MembershipExtractionFunction ──

        [LoggerMessage(EventId = 30040, Level = LogLevel.Information,
            Message = "Extracting membership information for {PartsCount} parts")]
        public static partial void ExtractingMembershipInfo(this ILogger logger, int partsCount);

        [LoggerMessage(EventId = 30041, Level = LogLevel.Information,
            Message = "Successfully extracted membership information with {SourceMemberCount} source members and {DestinationMemberCount} destination members")]
        public static partial void MembershipExtractionSuccess(this ILogger logger, int sourceMemberCount, int destinationMemberCount);

        [LoggerMessage(EventId = 30042, Level = LogLevel.Error,
            Message = "Error extracting membership information: {ErrorMessage}")]
        public static partial void MembershipExtractionError(this ILogger logger, Exception exception, string errorMessage);

        [LoggerMessage(EventId = 30043, Level = LogLevel.Warning,
            Message = "Failed to process file '{FilePath}': {ErrorMessage}")]
        public static partial void FileProcessingFailed(this ILogger logger, Exception exception, string filePath, string errorMessage);

        [LoggerMessage(EventId = 30044, Level = LogLevel.Warning,
            Message = "JSON deserialization failed for file '{FilePath}': {ErrorMessage}")]
        public static partial void JsonDeserializationFailed(this ILogger logger, Exception exception, string filePath, string errorMessage);

        [LoggerMessage(EventId = 30045, Level = LogLevel.Information,
            Message = "Processing destination membership file: {FilePath}, Content length: {ContentLength}")]
        public static partial void ProcessingDestinationFile(this ILogger logger, string filePath, int contentLength);

        [LoggerMessage(EventId = 30046, Level = LogLevel.Warning,
            Message = "Failed to process destination file '{FilePath}': {ErrorMessage}")]
        public static partial void DestinationFileProcessingFailed(this ILogger logger, Exception exception, string filePath, string errorMessage);

        [LoggerMessage(EventId = 30047, Level = LogLevel.Warning,
            Message = "JSON deserialization failed for destination file '{FilePath}': {ErrorMessage}")]
        public static partial void DestinationJsonDeserializationFailed(this ILogger logger, Exception exception, string filePath, string errorMessage);

        // ── DeltaCalculatorFunction ──

        [LoggerMessage(EventId = 30050, Level = LogLevel.Information,
            Message = "Source blob download result: {BlobStatus} for path {FilePath}")]
        public static partial void SourceBlobDownloadResult(this ILogger logger, string blobStatus, string filePath);

        [LoggerMessage(EventId = 30051, Level = LogLevel.Information,
            Message = "Destination blob download result: {BlobStatus} for path {FilePath}")]
        public static partial void DestinationBlobDownloadResult(this ILogger logger, string blobStatus, string filePath);

        [LoggerMessage(EventId = 30052, Level = LogLevel.Warning,
            Message = "SourceMembership blob not found")]
        public static partial void SourceBlobNotFound(this ILogger logger);

        [LoggerMessage(EventId = 30053, Level = LogLevel.Warning,
            Message = "DestinationMembership blob not found")]
        public static partial void DestinationBlobNotFound(this ILogger logger);

        // ── AggregatedMembershipUploaderFunction ──

        [LoggerMessage(EventId = 30060, Level = LogLevel.Information,
            Message = "Starting aggregated membership upload for GroupId {GroupId}")]
        public static partial void StartingAggregatedUpload(this ILogger logger, Guid groupId);

        [LoggerMessage(EventId = 30061, Level = LogLevel.Information,
            Message = "Aggregated membership file uploaded to {FilePath}")]
        public static partial void AggregatedUploadComplete(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 30062, Level = LogLevel.Error,
            Message = "Aggregated membership upload failed: {ErrorMessage}")]
        public static partial void AggregatedUploadFailed(this ILogger logger, Exception exception, string errorMessage);

        // ── File Operations ──

        [LoggerMessage(EventId = 30070, Level = LogLevel.Information,
            Message = "Uploading file {FilePath}")]
        public static partial void UploadingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 30071, Level = LogLevel.Information,
            Message = "Uploaded file {FilePath}")]
        public static partial void UploadedFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 30072, Level = LogLevel.Information,
            Message = "Downloading file {FilePath}")]
        public static partial void DownloadingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 30073, Level = LogLevel.Information,
            Message = "Downloaded file {FilePath}")]
        public static partial void DownloadedFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 30074, Level = LogLevel.Information,
            Message = "Deleting file {FilePath}")]
        public static partial void DeletingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 30075, Level = LogLevel.Information,
            Message = "Deleted file {FilePath}")]
        public static partial void DeletedFile(this ILogger logger, string filePath);

        // ── DeltaCalculatorService ──

        [LoggerMessage(EventId = 30100, Level = LogLevel.Warning,
            Message = "Sync job : Id {SyncJobId} was not found!")]
        public static partial void SyncJobNotFound(this ILogger logger, Guid syncJobId);

        [LoggerMessage(EventId = 30101, Level = LogLevel.Information,
            Message = "The Dry Run Enabled configuration is currently set to {IsDryRunSync}. We will not be syncing members if any of the 3 Dry Run Enabled configurations is set to True.")]
        public static partial void DryRunConfiguration(this ILogger logger, bool isDryRunSync);

        [LoggerMessage(EventId = 30102, Level = LogLevel.Information,
            Message = "Processing sync job : Id {SyncJobId}")]
        public static partial void ProcessingSyncJob(this ILogger logger, Guid syncJobId);

        [LoggerMessage(EventId = 30103, Level = LogLevel.Information,
            Message = "{GroupId} job's status is {Status}.")]
        public static partial void JobStatusInfo(this ILogger logger, Guid groupId, string status);

        [LoggerMessage(EventId = 30104, Level = LogLevel.Warning,
            Message = "When syncing {FromTo}, destination group {Destination} doesn't exist. Not syncing and marking as error.")]
        public static partial void DestinationGroupNotExists(this ILogger logger, string fromTo, string destination);

        [LoggerMessage(EventId = 30105, Level = LogLevel.Information,
            Message = "Calculating membership difference {FromTo}. Destination group has {DestinationMemberCount} users.")]
        public static partial void CalculatingMembershipDifference(this ILogger logger, string fromTo, int destinationMemberCount);

        [LoggerMessage(EventId = 30106, Level = LogLevel.Information,
            Message = "Calculated membership difference {FromTo} in {ElapsedSeconds} seconds. Adding {AddCount} users and removing {RemoveCount}.")]
        public static partial void CalculatedMembershipDifference(this ILogger logger, string fromTo, double elapsedSeconds, int addCount, int removeCount);

        [LoggerMessage(EventId = 30107, Level = LogLevel.Warning,
            Message = "Membership increase in {GroupId} is {PercentageIncrease}% and is greater than threshold value {ThresholdPercentage}%")]
        public static partial void AdditionsThresholdExceeded(this ILogger logger, Guid groupId, double percentageIncrease, int thresholdPercentage);

        [LoggerMessage(EventId = 30108, Level = LogLevel.Warning,
            Message = "Membership decrease in {GroupId} is {PercentageDecrease}% and is lesser than threshold value {ThresholdPercentage}%")]
        public static partial void RemovalsThresholdExceeded(this ILogger logger, Guid groupId, double percentageDecrease, int thresholdPercentage);

        [LoggerMessage(EventId = 30109, Level = LogLevel.Information,
            Message = "Going to sync the job even though threshold exceeded because IgnoreThresholdOnce is currently set to {IgnoreThresholdOnce}.")]
        public static partial void IgnoreThresholdOnceSync(this ILogger logger, bool ignoreThresholdOnce);

        [LoggerMessage(EventId = 30110, Level = LogLevel.Information,
            Message = "Going to sync the job even though threshold exceeded because AllowEmptyDestination is currently set to {AllowEmptyDestination}.")]
        public static partial void AllowEmptyDestinationSync(this ILogger logger, bool allowEmptyDestination);

        [LoggerMessage(EventId = 30111, Level = LogLevel.Warning,
            Message = "Threshold exceeded, no changes made to group {GroupName} ({GroupId}).")]
        public static partial void ThresholdExceededNoChanges(this ILogger logger, string groupName, Guid groupId);

        [LoggerMessage(EventId = 30112, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentNotificationQueueMessage(this ILogger logger, string messageId);

        // ── GraphAPIService ──

        [LoggerMessage(EventId = 30120, Level = LogLevel.Warning,
            Message = "Got a transient SocketException. Retrying. This was try {SleepDuration} out of {MaxRetries}.")]
        public static partial void TransientSocketException(this ILogger logger, Exception exception, TimeSpan sleepDuration, int maxRetries);

        [LoggerMessage(EventId = 30121, Level = LogLevel.Information,
            Message = "Sent message {MessageId} to service bus notifications queue")]
        public static partial void SentGraphNotificationMessage(this ILogger logger, string messageId);

        // ── TopicMessageSenderService ──

        [LoggerMessage(EventId = 30130, Level = LogLevel.Information,
            Message = "Sent message to {MembershipType} membership updater")]
        public static partial void SentToMembershipUpdater(this ILogger logger, string membershipType);

        [LoggerMessage(EventId = 30131, Level = LogLevel.Information,
            Message = "Sent message to {LaneSize} lane with {MembersToBeUpdated} operations.")]
        public static partial void SentToLane(this ILogger logger, string laneSize, int membersToBeUpdated);
    }
}
