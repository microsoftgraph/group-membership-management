// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.AzureUserReader
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle (60000–60009) ──

        [LoggerMessage(EventId = 60000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 60001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 60002, Level = LogLevel.Error,
            Message = "{FunctionName} failed with exception")]
        public static partial void FunctionFailed(this ILogger logger, string functionName, Exception exception);

        // ── StarterFunction (60010–60019) ──

        [LoggerMessage(EventId = 60010, Level = LogLevel.Warning,
            Message = "Request body was not provided.")]
        public static partial void RequestBodyNotProvided(this ILogger logger);

        [LoggerMessage(EventId = 60011, Level = LogLevel.Warning,
            Message = "Request body is not valid.")]
        public static partial void RequestBodyNotValid(this ILogger logger);

        [LoggerMessage(EventId = 60012, Level = LogLevel.Warning,
            Message = "Request body is not valid. TenantInformation is missing.")]
        public static partial void TenantInformationMissing(this ILogger logger);

        [LoggerMessage(EventId = 60013, Level = LogLevel.Error,
            Message = "Unexpected error occurred when processing the request.")]
        public static partial void UnexpectedRequestError(this ILogger logger, Exception exception);

        // ── AzureUserCreatorFunction (60020–60029) ──

        [LoggerMessage(EventId = 60020, Level = LogLevel.Warning,
            Message = "{FunctionName} exception, request or TenantInformation is null")]
        public static partial void RequestOrTenantInfoNull(this ILogger logger, string functionName);

        // ── UserReaderSubOrchestratorFunction (60030–60039) ──

        [LoggerMessage(EventId = 60030, Level = LogLevel.Information,
            Message = "Retrieved {UserCount} users so far!")]
        public static partial void UsersRetrievedSoFar(this ILogger logger, int userCount);

        [LoggerMessage(EventId = 60031, Level = LogLevel.Information,
            Message = "Retrieved a total of {UserCount} users")]
        public static partial void TotalUsersRetrieved(this ILogger logger, int userCount);

        // ── UserCreatorSubOrchestratorFunction (60040–60049) ──

        [LoggerMessage(EventId = 60040, Level = LogLevel.Information,
            Message = "Creating {UserCount} new users.")]
        public static partial void CreatingNewUsers(this ILogger logger, int userCount);

        [LoggerMessage(EventId = 60041, Level = LogLevel.Information,
            Message = "No personnel numbers provided. Skipping user creation.")]
        public static partial void NoPersonnelNumbersProvided(this ILogger logger);

        [LoggerMessage(EventId = 60042, Level = LogLevel.Information,
            Message = "Processing {ProcessedCount} out of {TotalCount} users.")]
        public static partial void ProcessingUsers(this ILogger logger, int processedCount, int totalCount);

        [LoggerMessage(EventId = 60043, Level = LogLevel.Information,
            Message = "UserCreatorRequest: {RequestJson}")]
        public static partial void UserCreatorRequestDetails(this ILogger logger, string requestJson);

        [LoggerMessage(EventId = 60044, Level = LogLevel.Information,
            Message = "Created {UserCount} new users.")]
        public static partial void NewUsersCreated(this ILogger logger, int userCount);

        // ── AzureUserReaderService (60050–60059) ──

        [LoggerMessage(EventId = 60050, Level = LogLevel.Information,
            Message = "Retrieved {Count} personnel numbers.")]
        public static partial void PersonnelNumbersRetrieved(this ILogger logger, int count);

        [LoggerMessage(EventId = 60051, Level = LogLevel.Information,
            Message = "Uploaded {Count} user ids.")]
        public static partial void UserIdsUploaded(this ILogger logger, int count);

        [LoggerMessage(EventId = 60052, Level = LogLevel.Warning,
            Message = "File not found {BlobPath}.")]
        public static partial void FileNotFound(this ILogger logger, Uri blobPath);

        [LoggerMessage(EventId = 60053, Level = LogLevel.Error,
            Message = "An error occurred while downloading the file. StatusCode: {StatusCode}, Reason: {ReasonPhrase}")]
        public static partial void FileDownloadError(this ILogger logger, int statusCode, string reasonPhrase);

        // ── Demo agents (60060–60069) ──

        [LoggerMessage(EventId = 60060, Level = LogLevel.Warning,
            Message = "Requested {Requested} demo agents but only {Capacity} can be assigned to the available users.")]
        public static partial void DemoAgentCapacityShortfall(this ILogger logger, int requested, int capacity);

        [LoggerMessage(EventId = 60061, Level = LogLevel.Information,
            Message = "Demo agents: Requested={Requested}, Capacity={Capacity}, Expected={Expected}, AlreadyPresent={AlreadyPresent}, Created={Created}, ManagersFixed={ManagersFixed}, ManagerFailures={ManagerFailures}, Failed={Failed}.")]
        public static partial void DemoAgentSummary(this ILogger logger, int requested, int capacity, int expected, int alreadyPresent, int created, int managersFixed, int managerFailures, int failed);

        [LoggerMessage(EventId = 60062, Level = LogLevel.Error,
            Message = "Failed to create demo agent {UserPrincipalName}.")]
        public static partial void DemoAgentCreateFailed(this ILogger logger, string userPrincipalName, Exception ex);

        [LoggerMessage(EventId = 60063, Level = LogLevel.Warning,
            Message = "Setting manager for demo agent {AgentId} failed on attempt {Attempt}: {Reason}")]
        public static partial void DemoAgentManagerRetry(this ILogger logger, string agentId, int attempt, string reason);

        [LoggerMessage(EventId = 60064, Level = LogLevel.Error,
            Message = "Failed to set manager for demo agent {AgentId}.")]
        public static partial void DemoAgentManagerFailed(this ILogger logger, string agentId, Exception ex);

        [LoggerMessage(EventId = 60065, Level = LogLevel.Warning,
            Message = "Demo agent provisioning completed with failures: Created={Created}, ManagersFixed={ManagersFixed}, CreationFailures={CreationFailures}, ManagerFailures={ManagerFailures}.")]
        public static partial void DemoAgentProvisioningDegraded(this ILogger logger, int created, int managersFixed, int creationFailures, int managerFailures);
    }
}
