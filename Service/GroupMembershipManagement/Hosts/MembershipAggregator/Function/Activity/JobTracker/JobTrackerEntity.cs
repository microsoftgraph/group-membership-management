// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobTrackerEntity : TaskEntity<JobState>, IJobTracker
    {
        public JobTrackerEntity()
        {
            // Required for tests that instantiate the entity directly.
            State = new JobState();
        }

        protected override JobState InitializeState(TaskEntityOperation operation)
        {
            return new JobState();
        }

        // Atomic register-and-check.
        public Task<JobTrackerCompletionResult> RegisterPartAndCheckComplete(JobTrackerRegistration registration)
        {
            if (registration == null)
            {
                return Task.FromResult(CreateCompletionResult(false));
            }

            if (State.TotalParts == 0)
            {
                State.TotalParts = registration.TotalParts;
            }

            // Register by PartNumber. Dictionary semantics make double-registration
            // safe and let us carry the blob path on the same key.
            State.CompletedParts[registration.PartNumber] = registration.FilePath;

            if (registration.IsDestinationPart)
            {
                State.DestinationPart = registration.FilePath;
            }

            var observedCount = State.CompletedParts.Count;
            var allPartsPresent = State.TotalParts > 0 && observedCount >= State.TotalParts;

            // Single-writer completion claim: only the first caller that observes
            // all parts present ever gets IsComplete=true.
            var isComplete = false;
            if (allPartsPresent && !State.CompletionClaimed)
            {
                State.CompletionClaimed = true;
                isComplete = true;
            }

            return Task.FromResult(CreateCompletionResult(isComplete));
        }

        private JobTrackerCompletionResult CreateCompletionResult(bool isComplete)
        {
            return new JobTrackerCompletionResult
            {
                TotalParts = State.TotalParts,
                CompletedCount = State.CompletedParts.Count,
                IsComplete = isComplete,
                CompletedParts = isComplete
                    ? new Dictionary<int, string>(State.CompletedParts)
                    : new Dictionary<int, string>(),
                DestinationPart = isComplete ? State.DestinationPart : null
            };
        }

        [Function(nameof(JobTrackerEntity))]
        public static Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher ctx)
        {
            return ctx.DispatchAsync<JobTrackerEntity>();
        }
    }
}
