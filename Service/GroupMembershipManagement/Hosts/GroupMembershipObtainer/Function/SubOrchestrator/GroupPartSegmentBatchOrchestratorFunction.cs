// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>Input of one child orchestration: up to 100 segments.</summary>
    public sealed class GroupPartSegmentBatchRequest
    {
        public GroupPartSelectionRequest Request { get; set; }
        public List<int> SegmentIndices { get; set; } = new List<int>();
    }

    /// <summary>Output of one child orchestration: the segment results, or why classification stopped.</summary>
    public sealed class GroupPartSegmentBatchResult
    {
        public List<GroupPartSegmentResult> Results { get; set; } = new List<GroupPartSegmentResult>();

        /// <summary>Null when every segment completed or <see cref="RetriesExhausted"/> is set; otherwise the fallback reason.</summary>
        public string FallbackReason { get; set; }

        /// <summary>
        /// Null unless a segment's lookup still failed after the shared retry policies' attempts. Then the part fails
        /// without a fallback read and the job reaches TransientError.
        /// </summary>
        public string RetriesExhausted { get; set; }
    }

    /// <summary>
    /// Classifies a group of up to 100 segments in its own orchestration instance, so each instance's history stays
    /// bounded.
    /// </summary>
    public sealed class GroupPartSegmentBatchOrchestratorFunction
    {
        public const int MaximumSegmentsPerOrchestration = 100;

        [Function(nameof(GroupPartSegmentBatchOrchestratorFunction))]
        public async Task<GroupPartSegmentBatchResult> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var input = context.GetInput<GroupPartSegmentBatchRequest>()
                ?? throw new InvalidOperationException("The segment batch orchestration requires its input.");
            if (input.SegmentIndices == null || input.SegmentIndices.Count > MaximumSegmentsPerOrchestration)
            {
                throw new ArgumentException($"A segment batch holds 1 to {MaximumSegmentsPerOrchestration} segments.");
            }

            return await SegmentClassificationLoop.ClassifyAsync(
                context,
                input.Request,
                input.SegmentIndices);
        }
    }

    /// <summary>
    /// Classifies segments in order with the classify activity, one call per segment. <c>RetriesExhausted</c> stops with
    /// <see cref="GroupPartSegmentBatchResult.RetriesExhausted"/> set (the part fails and the job reaches TransientError),
    /// and so does a classify call that fails with retry exhaustion or the function timeout.
    /// <c>LookupUnavailable</c> stops with <c>lookup forbidden</c>; <c>Failed</c> with <c>identification failed</c>.
    /// Any other thrown activity failure propagates as <see cref="TaskFailedException"/>.
    /// </summary>
    internal static class SegmentClassificationLoop
    {
        public const string LookupForbidden = "lookup forbidden";
        public const string IdentificationFailed = "identification failed";

        public static async Task<GroupPartSegmentBatchResult> ClassifyAsync(
            TaskOrchestrationContext context,
            GroupPartSelectionRequest request,
            IReadOnlyList<int> segments)
        {
            var result = new GroupPartSegmentBatchResult();
            foreach (var segment in segments)
            {
                var segmentRequest = JsonSerializer.Deserialize<GroupPartSelectionRequest>(JsonSerializer.Serialize(request));
                segmentRequest.SegmentIndex = segment;
                GroupPartSegmentResult segmentResult;
                try
                {
                    segmentResult = await context.CallActivityAsync<GroupPartSegmentResult>(
                        nameof(GroupPartSegmentClassifyFunction),
                        segmentRequest);
                }
                catch (TaskFailedException exception) when (OrchestratorFunction.IsRetryExhaustion(exception))
                {
                    // A classify call cut short by the function timeout fails as a task instead of returning an outcome.
                    // Report it in the result, so a child orchestration does not depend on Durable's nested failure details.
                    result.RetriesExhausted = $"segment {segment}: {exception.FailureDetails?.ErrorType ?? exception.Message}";
                    return result;
                }

                var outcome = segmentResult?.Outcome;
                if (outcome?.Status == SelectionOutcomeStatus.Completed)
                {
                    result.Results.Add(segmentResult);
                    continue;
                }

                if (outcome?.Status == SelectionOutcomeStatus.RetriesExhausted)
                {
                    result.RetriesExhausted = $"segment {segment}: {outcome.Reason}";
                    return result;
                }

                result.FallbackReason = outcome?.Status == SelectionOutcomeStatus.LookupUnavailable
                    ? LookupForbidden
                    : IdentificationFailed;
                return result;
            }

            return result;
        }
    }
}
