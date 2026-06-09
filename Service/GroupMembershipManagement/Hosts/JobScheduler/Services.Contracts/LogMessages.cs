// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.JobScheduler
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 40000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 40001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── Orchestrator ──

        [LoggerMessage(EventId = 40002, Level = LogLevel.Information,
            Message = "{FunctionName} function started at: {StartTime}")]
        public static partial void OrchestratorStarted(this ILogger logger, string functionName, DateTime startTime);

        [LoggerMessage(EventId = 40003, Level = LogLevel.Information,
            Message = "{FunctionName} function completed at: {CompletionTime}")]
        public static partial void OrchestratorCompleted(this ILogger logger, string functionName, DateTime completionTime);

        [LoggerMessage(EventId = 40004, Level = LogLevel.Information,
            Message = "{FunctionName} function completed immediately at: {CompletionTime} due to Reset and Distribute set to false")]
        public static partial void OrchestratorCompletedImmediately(this ILogger logger, string functionName, DateTime completionTime);

        [LoggerMessage(EventId = 40005, Level = LogLevel.Information,
            Message = "Successfully reset jobs to update.")]
        public static partial void JobsResetSuccessfully(this ILogger logger);

        [LoggerMessage(EventId = 40006, Level = LogLevel.Information,
            Message = "Successfully distributed jobs to update.")]
        public static partial void JobsDistributedSuccessfully(this ILogger logger);

        [LoggerMessage(EventId = 40007, Level = LogLevel.Information,
            Message = "Successfully updated all jobs accordingly.")]
        public static partial void JobsUpdatedSuccessfully(this ILogger logger);

        // ── GetJobsSubOrchestrator ──

        [LoggerMessage(EventId = 40010, Level = LogLevel.Information,
            Message = "Retrieving enabled sync jobs")]
        public static partial void RetrievingEnabledSyncJobs(this ILogger logger);

        [LoggerMessage(EventId = 40011, Level = LogLevel.Information,
            Message = "Retrieved {JobCount} enabled sync jobs")]
        public static partial void RetrievedEnabledSyncJobs(this ILogger logger, int jobCount);

        // ── UpdateJobsSubOrchestrator ──

        [LoggerMessage(EventId = 40020, Level = LogLevel.Information,
            Message = "Updating {JobCount} total jobs...")]
        public static partial void UpdatingTotalJobs(this ILogger logger, int jobCount);

        [LoggerMessage(EventId = 40021, Level = LogLevel.Information,
            Message = "Updated {JobCount} total jobs.")]
        public static partial void UpdatedTotalJobs(this ILogger logger, int jobCount);

        // ── JobSchedulingService ──

        [LoggerMessage(EventId = 40030, Level = LogLevel.Information,
            Message = "Updating {JobCount} jobs to have ScheduledDate of {NewStartTime}")]
        public static partial void UpdatingJobsScheduledDate(this ILogger logger, int jobCount, DateTime newStartTime);

        [LoggerMessage(EventId = 40031, Level = LogLevel.Information,
            Message = "Updated {JobCount} jobs to have ScheduledDate of {NewStartTime}")]
        public static partial void UpdatedJobsScheduledDate(this ILogger logger, int jobCount, DateTime newStartTime);

        [LoggerMessage(EventId = 40032, Level = LogLevel.Information,
            Message = "Distributing {JobCount} jobs (PrioritizeThresholdJobs: {PrioritizeThresholdJobs})")]
        public static partial void DistributingJobs(this ILogger logger, int jobCount, bool prioritizeThresholdJobs);

        [LoggerMessage(EventId = 40033, Level = LogLevel.Information,
            Message = "Distributed {JobCount} jobs")]
        public static partial void DistributedJobs(this ILogger logger, int jobCount);

        [LoggerMessage(EventId = 40034, Level = LogLevel.Information,
            Message = "Calculating distribution for jobs with period {PeriodInHours}")]
        public static partial void CalculatingDistribution(this ILogger logger, int periodInHours);

        [LoggerMessage(EventId = 40035, Level = LogLevel.Information,
            Message = "Sorting jobs WITH threshold prioritization (jobs with thresholds will be scheduled first)")]
        public static partial void SortingWithThresholdPrioritization(this ILogger logger);

        [LoggerMessage(EventId = 40036, Level = LogLevel.Information,
            Message = "Sorting jobs WITHOUT threshold prioritization (normal sort order by Status and LastRunTime)")]
        public static partial void SortingWithoutThresholdPrioritization(this ILogger logger);

        [LoggerMessage(EventId = 40037, Level = LogLevel.Information,
            Message = "Calculated {ConcurrencyNumber} thread count for jobs with period {PeriodInHours}")]
        public static partial void CalculatedThreadCount(this ILogger logger, int concurrencyNumber, int periodInHours);

        // ── ApplicationService ──

        [LoggerMessage(EventId = 40040, Level = LogLevel.Information,
            Message = "Configuration set to not reset or update so doing nothing.")]
        public static partial void ConfigurationSetToDoNothing(this ILogger logger);

        [LoggerMessage(EventId = 40041, Level = LogLevel.Information,
            Message = "Resetting {JobCount} jobs to have ScheduledDate of {NewStartTime}")]
        public static partial void ResettingJobs(this ILogger logger, int jobCount, DateTime newStartTime);

        [LoggerMessage(EventId = 40042, Level = LogLevel.Information,
            Message = "Reset {JobCount} jobs to have ScheduledDate of {NewStartTime}")]
        public static partial void ResetJobs(this ILogger logger, int jobCount, DateTime newStartTime);

        [LoggerMessage(EventId = 40043, Level = LogLevel.Information,
            Message = "Distributing {JobCount} jobs")]
        public static partial void DistributingJobsInApplicationService(this ILogger logger, int jobCount);

        [LoggerMessage(EventId = 40044, Level = LogLevel.Information,
            Message = "Distributed {JobCount} jobs")]
        public static partial void DistributedJobsInApplicationService(this ILogger logger, int jobCount);

        // ── CheckJobSchedulerStatusFunction ──

        [LoggerMessage(EventId = 40050, Level = LogLevel.Information,
            Message = "Response content for status check is: {ResponseContent}")]
        public static partial void StatusCheckResponse(this ILogger logger, string responseContent);

        [LoggerMessage(EventId = 40051, Level = LogLevel.Information,
            Message = "Status of JobScheduler has been verified as completed at {CompletionTime}")]
        public static partial void JobSchedulerCompleted(this ILogger logger, DateTime completionTime);

        [LoggerMessage(EventId = 40052, Level = LogLevel.Information,
            Message = "Status of JobScheduler is still pending at {CheckTime}")]
        public static partial void JobSchedulerPending(this ILogger logger, DateTime checkTime);

        // ── PostCallbackFunction ──

        [LoggerMessage(EventId = 40060, Level = LogLevel.Information,
            Message = "Successfully posted to url '{CallbackUrl}' with following body: {SuccessBody}")]
        public static partial void PostCallbackSuccessful(this ILogger logger, string callbackUrl, string successBody);
    }
}
