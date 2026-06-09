// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.NonProdService
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 70000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 70001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 70010, Level = LogLevel.Error,
            Message = "Error with {FunctionName}, check exception")]
        public static partial void ErrorWithFunction(this ILogger logger, string functionName);

        // ── IntegrationTestingPrepSubOrchestratorFunction ──

        [LoggerMessage(EventId = 70020, Level = LogLevel.Information,
            Message = "Insufficient users in tenant. {ActualUserCount} is less than the minimum requirement of {RequiredUserCount} users.")]
        public static partial void InsufficientUsersInTenant(this ILogger logger, int actualUserCount, int requiredUserCount);

        [LoggerMessage(EventId = 70021, Level = LogLevel.Information,
            Message = "Creating, if nonexistent, and populating, if not properly populated, group with name {GroupName}")]
        public static partial void CreatingAndPopulatingGroup(this ILogger logger, string groupName);

        [LoggerMessage(EventId = 70022, Level = LogLevel.Information,
            Message = "Calculated membership difference for {GroupName}: Must add {UsersToAdd} users and remove {UsersToRemove} users.")]
        public static partial void CalculatedMembershipDifference(this ILogger logger, string groupName, int usersToAdd, int usersToRemove);

        // ── LoadTestingPrepSubOrchestratorFunction ──

        [LoggerMessage(EventId = 70030, Level = LogLevel.Information,
            Message = "No groups to create sync jobs for")]
        public static partial void NoGroupsToCreateSyncJobsFor(this ILogger logger);

        [LoggerMessage(EventId = 70031, Level = LogLevel.Information,
            Message = "Creating groups: size={GroupSize}, batch offset={Offset}, count={Count}, total={Total}")]
        public static partial void CreatingGroupsBatch(this ILogger logger, int groupSize, int offset, int count, int total);

        [LoggerMessage(EventId = 70032, Level = LogLevel.Information,
            Message = "Waiting 30s for Graph API replication before ensuring ownership of {GroupCount} newly created groups")]
        public static partial void WaitingForGraphReplication(this ILogger logger, int groupCount);

        [LoggerMessage(EventId = 70033, Level = LogLevel.Information,
            Message = "Ensuring ownership: batch {BatchNumber}/{TotalBatches} ({GroupCount} groups)")]
        public static partial void EnsuringOwnershipBatch(this ILogger logger, int batchNumber, int totalBatches, int groupCount);

        [LoggerMessage(EventId = 70034, Level = LogLevel.Information,
            Message = "Ownership ensured: {AlreadyOwned} already owned, {NewlyOwned} newly owned, {Failed} failed")]
        public static partial void OwnershipEnsured(this ILogger logger, int alreadyOwned, int newlyOwned, int failed);

        // ── GroupCreatorAndRetrieverFunction ──

        [LoggerMessage(EventId = 70040, Level = LogLevel.Information,
            Message = "{FunctionName} function failed because group couldn't be generated")]
        public static partial void GroupCouldNotBeGenerated(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 70041, Level = LogLevel.Information,
            Message = "Successfully created group with name {GroupName}, if it did not exist already")]
        public static partial void SuccessfullyCreatedGroup(this ILogger logger, string groupName);

        // ── GroupCreatorAndRetrieverBatchFunction ──

        [LoggerMessage(EventId = 70050, Level = LogLevel.Debug,
            Message = "{FunctionName} creating group {GroupName}")]
        public static partial void CreatingGroup(this ILogger logger, string functionName, string groupName);

        [LoggerMessage(EventId = 70051, Level = LogLevel.Warning,
            Message = "{FunctionName} failed to create group {GroupName}. Retrying...")]
        public static partial void FailedToCreateGroupRetrying(this ILogger logger, string functionName, string groupName);

        [LoggerMessage(EventId = 70052, Level = LogLevel.Error,
            Message = "{FunctionName} failed to create group {GroupName} after multiple attempts")]
        public static partial void FailedToCreateGroupAfterRetries(this ILogger logger, string functionName, string groupName);

        [LoggerMessage(EventId = 70053, Level = LogLevel.Debug,
            Message = "Skipping group {GroupName} — already exists in known group list")]
        public static partial void SkippingExistingGroup(this ILogger logger, string groupName);

        [LoggerMessage(EventId = 70054, Level = LogLevel.Information,
            Message = "Successfully created group {GroupName}")]
        public static partial void GroupCreatedSuccessfully(this ILogger logger, string groupName);

        [LoggerMessage(EventId = 70055, Level = LogLevel.Error,
            Message = "Failed to create group {GroupName}")]
        public static partial void FailedToCreateGroup(this ILogger logger, string groupName);

        // ── LoadTestingGroupCalculatorFunction ──

        [LoggerMessage(EventId = 70060, Level = LogLevel.Debug,
            Message = "Number of existing Load Test groups found: {GroupCount}")]
        public static partial void ExistingLoadTestGroupsFound(this ILogger logger, int groupCount);

        [LoggerMessage(EventId = 70061, Level = LogLevel.Debug,
            Message = "Target Distribution: {Distribution}")]
        public static partial void TargetDistribution(this ILogger logger, string distribution);

        [LoggerMessage(EventId = 70062, Level = LogLevel.Debug,
            Message = "Existing Groups: {ExistingGroups}")]
        public static partial void ExistingGroups(this ILogger logger, string existingGroups);

        [LoggerMessage(EventId = 70063, Level = LogLevel.Debug,
            Message = "{Message}")]
        public static partial void GroupsToBeCreated(this ILogger logger, string message);

        // ── LoadTestingSyncJobCreatorFunction ──

        [LoggerMessage(EventId = 70070, Level = LogLevel.Information,
            Message = "SyncJobs to be created: {SyncJobCounts}")]
        public static partial void SyncJobsToBeCreated(this ILogger logger, string syncJobCounts);

        // ── SyncJobCheckerFunction ──

        [LoggerMessage(EventId = 70080, Level = LogLevel.Information,
            Message = "Group size: {GroupSize}, Expected Group Count: {ExpectedCount}, Existing Group Count: {ExistingCount}, Existing Job Count: {ExistingJobCount}, Missing Job Count: {MissingCount}")]
        public static partial void SyncJobCheckerGroupStats(this ILogger logger, int groupSize, int expectedCount, int existingCount, int existingJobCount, int missingCount);
        // ── GroupUpdaterSubOrchestratorFunction ──

        [LoggerMessage(EventId = 70090, Level = LogLevel.Information,
            Message = "{ActionType} {SuccessCount}/{TotalCount} users so far.")]
        public static partial void GroupUpdaterProgress(this ILogger logger, string actionType, int successCount, int totalCount);

        [LoggerMessage(EventId = 70091, Level = LogLevel.Information,
            Message = "{ActionType} {TotalUsers} users.")]
        public static partial void GroupUpdaterCompleted(this ILogger logger, string actionType, int totalUsers);

        // —— EnsureGroupOwnershipFunction ——

        [LoggerMessage(EventId = 70100, Level = LogLevel.Debug,
            Message = "EnsureGroupOwnership started. Ensuring ownership for {GroupCount} managed group(s)")]
        public static partial void EnsureOwnershipStarted(this ILogger logger, int groupCount);

        [LoggerMessage(EventId = 70101, Level = LogLevel.Debug,
            Message = "EnsureGroupOwnership completed. Already owned: {AlreadyOwned}, newly owned: {NewlyOwned}, failed: {Failed}")]
        public static partial void EnsureOwnershipCompleted(this ILogger logger, int alreadyOwned, int newlyOwned, int failed);

        [LoggerMessage(EventId = 70102, Level = LogLevel.Debug,
            Message = "Resolved owner AppId {AppId} to ObjectId {ObjectId}")]
        public static partial void ResolvedOwnerAppId(this ILogger logger, Guid appId, Guid objectId);

        [LoggerMessage(EventId = 70103, Level = LogLevel.Debug,
            Message = "Ownership diff: {TotalManaged} total managed, {AlreadyOwned} already owned, {Unowned} unowned")]
        public static partial void OwnershipDiff(this ILogger logger, int totalManaged, int alreadyOwned, int unowned);

        [LoggerMessage(EventId = 70104, Level = LogLevel.Warning,
            Message = "Failed to add owner to group {GroupId}: {ErrorMessage}")]
        public static partial void FailedToAddOwner(this ILogger logger, Guid groupId, string errorMessage);

        [LoggerMessage(EventId = 70105, Level = LogLevel.Debug,
            Message = "Ownership progress: {Completed}/{Total} groups assigned ({Failed} failed)")]
        public static partial void OwnershipProgress(this ILogger logger, int completed, int total, int failed);
    }
}
