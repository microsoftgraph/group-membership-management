// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.MessageSplitter
{
    [ExcludeFromCodeCoverage]
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle (120000-120001) ──

        [LoggerMessage(EventId = 120000, Level = LogLevel.Information,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 120001, Level = LogLevel.Information,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── StarterFunction (120010-120019) ──

        [LoggerMessage(EventId = 120010, Level = LogLevel.Error,
            Message = "Unexpected error {ErrorMessage}")]
        public static partial void StarterUnexpectedError(this ILogger logger, Exception exception, string errorMessage);

        // ── OrchestratorFunction (120020-120029) ──

        [LoggerMessage(EventId = 120020, Level = LogLevel.Information,
            Message = "Processing message {MessageId}, by orchestrator instance {InstanceId}")]
        public static partial void ProcessingMessageByOrchestrator(this ILogger logger, string messageId, string instanceId);

        [LoggerMessage(EventId = 120021, Level = LogLevel.Error,
            Message = "Unexpected exception in orchestrator")]
        public static partial void OrchestratorUnexpectedException(this ILogger logger, Exception exception);

        // ── TopicMessageSenderFunction (120030-120039) ──

        [LoggerMessage(EventId = 120030, Level = LogLevel.Warning,
            Message = "Message {MessageId} exceeds the maximum batch size and will be sent individually.")]
        public static partial void MessageExceedsBatchSize(this ILogger logger, string messageId);

        [LoggerMessage(EventId = 120031, Level = LogLevel.Information,
            Message = "Sent {TotalSent} messages with {TotalOperations} total operations to {TargetSubscription} membership updater")]
        public static partial void SentMessagesToUpdater(this ILogger logger, int totalSent, int totalOperations, string targetSubscription);

        [LoggerMessage(EventId = 120032, Level = LogLevel.Information,
            Message = "Downloading file {FilePath}")]
        public static partial void DownloadingFile(this ILogger logger, string filePath);

        [LoggerMessage(EventId = 120033, Level = LogLevel.Information,
            Message = "Downloaded file {FilePath}")]
        public static partial void DownloadedFile(this ILogger logger, string filePath);

        // ── CompletionListener (120040-120049) ──

        [LoggerMessage(EventId = 120040, Level = LogLevel.Warning,
            Message = "Failed to parse completion signal: {ErrorMessage}")]
        public static partial void FailedToParseCompletionSignal(this ILogger logger, Exception exception, string errorMessage);

        [LoggerMessage(EventId = 120041, Level = LogLevel.Information,
            Message = "Processing completion signal; lane={LaneSize}")]
        public static partial void ProcessingCompletionSignal(this ILogger logger, string laneSize);

        [LoggerMessage(EventId = 120042, Level = LogLevel.Information,
            Message = "Completion processed; lane={LaneSize} released={Released} {DrainAction}")]
        public static partial void CompletionProcessed(this ILogger logger, string laneSize, bool released, string drainAction);

        // ── LeaseRenewListener (120050-120059) ──

        [LoggerMessage(EventId = 120050, Level = LogLevel.Warning,
            Message = "Failed to parse lease renew signal: {ErrorMessage}")]
        public static partial void FailedToParseLeaseRenewSignal(this ILogger logger, Exception exception, string errorMessage);

        [LoggerMessage(EventId = 120051, Level = LogLevel.Information,
            Message = "Processing lease renew signal; lane={LaneSize} leaseTimeoutMinutes={LeaseTimeoutMinutes}")]
        public static partial void ProcessingLeaseRenewSignal(this ILogger logger, string laneSize, int leaseTimeoutMinutes);

        [LoggerMessage(EventId = 120052, Level = LogLevel.Information,
            Message = "Lease renew signal ignored; no existing lease found. lane={LaneSize}")]
        public static partial void LeaseRenewIgnored(this ILogger logger, string laneSize);

        // ── PendingDispatcherFunction (120060-120069) ──

        [LoggerMessage(EventId = 120060, Level = LogLevel.Information,
            Message = "PendingDispatcherFunction received pending message; lane={LaneSize} seq={SequenceNumber}")]
        public static partial void PendingMessageReceived(this ILogger logger, string laneSize, long sequenceNumber);

        [LoggerMessage(EventId = 120061, Level = LogLevel.Warning,
            Message = "Failed to deserialize pending work item: {ErrorMessage}")]
        public static partial void PendingDeserializationFailed(this ILogger logger, Exception exception, string errorMessage);

        [LoggerMessage(EventId = 120062, Level = LogLevel.Information,
            Message = "Scheduled pending drain orchestrator; instanceId={InstanceId} lane={LaneSize} seq={SequenceNumber}")]
        public static partial void PendingDrainScheduled(this ILogger logger, string instanceId, string laneSize, long sequenceNumber);

        [LoggerMessage(EventId = 120063, Level = LogLevel.Warning,
            Message = "Failed to schedule orchestrator for pending message; lane={LaneSize} seq={SequenceNumber} deliveryCount={DeliveryCount} err={ErrorMessage}")]
        public static partial void PendingScheduleFailed(this ILogger logger, Exception exception, string laneSize, long sequenceNumber, int deliveryCount, string errorMessage);

        [LoggerMessage(EventId = 120064, Level = LogLevel.Warning,
            Message = "Max delivery count reached; setting job to Error and dead-lettering message; lane={LaneSize} seq={SequenceNumber}")]
        public static partial void PendingMaxDeliveryReached(this ILogger logger, string laneSize, long sequenceNumber);

        [LoggerMessage(EventId = 120065, Level = LogLevel.Information,
            Message = "Defer succeeded for pending message; lane={LaneSize} seq={SequenceNumber}")]
        public static partial void PendingDeferSucceeded(this ILogger logger, string laneSize, long sequenceNumber);

        [LoggerMessage(EventId = 120066, Level = LogLevel.Warning,
            Message = "Failed to defer pending message; lane={LaneSize} seq={SequenceNumber} deliveryCount={DeliveryCount} lockedUntilUtc={LockedUntilUtc} errType={ErrorType} err={ErrorMessage}")]
        public static partial void PendingDeferFailed(this ILogger logger, Exception exception, string laneSize, long sequenceNumber, int deliveryCount, string lockedUntilUtc, string errorType, string errorMessage);

        // ── DeferredPendingDrainOrchestrator (120070-120079) ──

        [LoggerMessage(EventId = 120070, Level = LogLevel.Information,
            Message = "DeferredPendingDrain: start lane={LaneSize} seq={SequenceNumber}")]
        public static partial void DrainStarted(this ILogger logger, string laneSize, long sequenceNumber);

        [LoggerMessage(EventId = 120072, Level = LogLevel.Information,
            Message = "DeferredPendingDrain: no items to process lane={LaneSize}")]
        public static partial void DrainNoItems(this ILogger logger, string laneSize);

        [LoggerMessage(EventId = 120073, Level = LogLevel.Information,
            Message = "DeferredPendingDrain: no capacity; stopping drain lane={LaneSize} inFlight={InFlightCount} runId={RunId} seq={SequenceNumber}")]
        public static partial void DrainNoCapacity(this ILogger logger, string laneSize, int inFlightCount, Guid runId, long sequenceNumber);

        [LoggerMessage(EventId = 120074, Level = LogLevel.Warning,
            Message = "DeferredPendingDrain: ReceiveDeferredPending failed; lane={LaneSize} runId={RunId} seq={SequenceNumber}")]
        public static partial void DrainReceiveFailed(this ILogger logger, Exception exception, string laneSize, Guid runId, long sequenceNumber);

        [LoggerMessage(EventId = 120075, Level = LogLevel.Information,
            Message = "DeferredPendingDrain: removing stale index entry (message not found, age={AgeMinutes}min); seq={SequenceNumber} jobId={JobId} lane={LaneSize}")]
        public static partial void DrainRemovingStaleEntry(this ILogger logger, string ageMinutes, long sequenceNumber, Guid jobId, string laneSize);

        [LoggerMessage(EventId = 120076, Level = LogLevel.Information,
            Message = "DeferredPendingDrain: item processed lane={LaneSize} seq={SequenceNumber} result={Result} messageNotFound={MessageNotFound}")]
        public static partial void DrainItemProcessed(this ILogger logger, string laneSize, long sequenceNumber, string result, bool messageNotFound);

        [LoggerMessage(EventId = 120077, Level = LogLevel.Information,
            Message = "DeferredPendingDrain: releasing {RemainingCount} remaining batch items on early exit; lane={LaneSize}")]
        public static partial void DrainReleasingRemainingItems(this ILogger logger, string laneSize, int remainingCount);

        // ── DeferredPendingEnqueueOrchestrator (120080-120089) ──

        [LoggerMessage(EventId = 120080, Level = LogLevel.Information,
            Message = "DeferredPendingEnqueue: indexed seq={SequenceNumber} lane={LaneSize}. Kicking drain.")]
        public static partial void EnqueueIndexed(this ILogger logger, long sequenceNumber, string laneSize);

        // ── DeferredPendingSweepFunction (120090-120099) ──

        [LoggerMessage(EventId = 120090, Level = LogLevel.Information,
            Message = "DeferredPendingSweep timer fired; scheduling sweep lane={LaneSize}")]
        public static partial void SweepTimerFired(this ILogger logger, string laneSize);

        [LoggerMessage(EventId = 120091, Level = LogLevel.Information,
            Message = "DeferredPendingSweep: start lane={LaneSize}")]
        public static partial void SweepStarted(this ILogger logger, string laneSize);

        [LoggerMessage(EventId = 120092, Level = LogLevel.Warning,
            Message = "DeferredPendingSweep: pruned stale index entry and set job to Error; seq={SequenceNumber} jobId={JobId} lane={LaneSize}")]
        public static partial void SweepPrunedStaleEntry(this ILogger logger, long sequenceNumber, Guid jobId, string laneSize);

        [LoggerMessage(EventId = 120093, Level = LogLevel.Information,
            Message = "DeferredPendingSweep: prunedExpiredLeases={PrunedLeases} prunedOldIndexItems={PrunedItems} lane={LaneSize}")]
        public static partial void SweepCompleted(this ILogger logger, int prunedLeases, int prunedItems, string laneSize);

        [LoggerMessage(EventId = 120094, Level = LogLevel.Information,
            Message = "DeferredPendingSweep: skipped pruning — downstream at capacity; activeLeases={ActiveLeases} maxInFlight={MaxInFlight} lane={LaneSize}")]
        public static partial void SweepSkippedAtCapacity(this ILogger logger, string laneSize, int activeLeases, int maxInFlight);

        // ── ReceiveDeferredPendingFunction (120100-120109) ──

        [LoggerMessage(EventId = 120100, Level = LogLevel.Warning,
            Message = "Deferred message not found (will {Action} index entry) seq={SequenceNumber}: {ErrorMessage}")]
        public static partial void DeferredMessageNotFound(this ILogger logger, string action, long sequenceNumber, string errorMessage);

        [LoggerMessage(EventId = 120101, Level = LogLevel.Warning,
            Message = "Failed to receive deferred message seq={SequenceNumber}: {ErrorMessage}")]
        public static partial void DeferredReceiveFailed(this ILogger logger, Exception exception, long sequenceNumber, string errorMessage);

        [LoggerMessage(EventId = 120102, Level = LogLevel.Warning,
            Message = "Deferred message not found (null) (will {Action} index entry) seq={SequenceNumber}")]
        public static partial void DeferredMessageNull(this ILogger logger, string action, long sequenceNumber);

        [LoggerMessage(EventId = 120103, Level = LogLevel.Warning,
            Message = "Dead-lettered deferred pending message due to null body; seq={SequenceNumber}")]
        public static partial void DeferredDeadLetteredNullBody(this ILogger logger, long sequenceNumber);

        [LoggerMessage(EventId = 120104, Level = LogLevel.Information,
            Message = "Dispatched deferred pending orchestration instanceId={InstanceId} seq={SequenceNumber}")]
        public static partial void DeferredDispatched(this ILogger logger, string instanceId, long sequenceNumber);

        [LoggerMessage(EventId = 120105, Level = LogLevel.Information,
            Message = "Completed deferred pending message seq={SequenceNumber}")]
        public static partial void DeferredCompleted(this ILogger logger, long sequenceNumber);

        [LoggerMessage(EventId = 120106, Level = LogLevel.Warning,
            Message = "Scheduled orchestration but failed to complete deferred message seq={SequenceNumber}: {ErrorMessage}")]
        public static partial void DeferredCompleteFailed(this ILogger logger, long sequenceNumber, string errorMessage);

        [LoggerMessage(EventId = 120107, Level = LogLevel.Warning,
            Message = "Dead-lettered deferred pending message due to invalid payload; seq={SequenceNumber} err={ErrorMessage}")]
        public static partial void DeferredDeadLetteredInvalidPayload(this ILogger logger, Exception exception, long sequenceNumber, string errorMessage);

        // ── OrchestratorFunction Reliability (120110-120119) ──

        [LoggerMessage(EventId = 120110, Level = LogLevel.Warning,
            Message = "OrchestratorFunction released RunLimiter lease after failure; runId={RunId} lane={LaneSize}")]
        public static partial void OrchestratorReleasedLease(this ILogger logger, Guid runId, string laneSize);

        [LoggerMessage(EventId = 120111, Level = LogLevel.Error,
            Message = "OrchestratorFunction failed to update job status to Error after TopicMessageSender failure")]
        public static partial void OrchestratorStatusUpdateFailed(this ILogger logger, Exception exception);

        // ── DeferredPendingSweep Reliability (120095-120099) ──

        [LoggerMessage(EventId = 120095, Level = LogLevel.Warning,
            Message = "DeferredPendingSweep: failed to set Error status for pruned item; seq={SequenceNumber} jobId={JobId} lane={LaneSize} err={ErrorMessage}")]
        public static partial void SweepJobStatusUpdateFailed(this ILogger logger, long sequenceNumber, Guid jobId, string laneSize, string errorMessage);

        [LoggerMessage(EventId = 120096, Level = LogLevel.Warning,
            Message = "DeferredPendingSweep: {FailedCount}/{TotalCount} job status updates failed; lane={LaneSize}")]
        public static partial void SweepStatusUpdateFailures(this ILogger logger, int failedCount, int totalCount, string laneSize);
    }
}
