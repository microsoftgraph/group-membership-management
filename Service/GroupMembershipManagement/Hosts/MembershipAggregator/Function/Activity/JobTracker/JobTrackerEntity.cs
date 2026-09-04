// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;
using System;
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
            if (State.RegistrationRejected)
            {
                if (string.IsNullOrWhiteSpace(State.RegistrationRejectionReason))
                {
                    return RejectRegistration(
                        "Job tracker has a rejected registration without a rejection reason.");
                }

                return Task.FromResult(CreateCompletionResult(false));
            }

            var registrationRejectionReason = GetRegistrationRejectionReason(registration);
            if (registrationRejectionReason != null)
            {
                return RejectRegistration(registrationRejectionReason);
            }

            var expectedTotalParts = State.TotalParts > 0
                ? State.TotalParts
                : registration.TotalParts;

            if (State.TotalParts > 0 && registration.TotalParts != State.TotalParts)
            {
                return RejectRegistration(
                    $"Cannot register part {registration.PartNumber}: TotalParts={registration.TotalParts} conflicts with expected {State.TotalParts}.");
            }

            var stateRejectionReason = GetStateRejectionReason(expectedTotalParts);
            if (stateRejectionReason != null)
            {
                return RejectRegistration(stateRejectionReason);
            }

            var partAlreadyRegistered = State.CompletedParts.TryGetValue(
                registration.PartNumber,
                out var existingPath);
            if (partAlreadyRegistered)
            {
                if (!string.Equals(existingPath, registration.FilePath, StringComparison.Ordinal))
                {
                    return RejectRegistration(
                        $"Cannot register part {registration.PartNumber} at '{registration.FilePath}': it is already registered at '{existingPath}'.");
                }

                var existingIsDestination = string.Equals(
                    State.DestinationPart,
                    existingPath,
                    StringComparison.Ordinal);
                if (registration.IsDestinationPart != existingIsDestination)
                {
                    return RejectRegistration(
                        $"Cannot register part {registration.PartNumber}: its destination marker conflicts with the existing registration.");
                }
            }
            else
            {
                foreach (var completedPart in State.CompletedParts)
                {
                    if (string.Equals(completedPart.Value, registration.FilePath, StringComparison.Ordinal))
                    {
                        return RejectRegistration(
                            $"Cannot register part {registration.PartNumber} at '{registration.FilePath}': the path is already registered for part {completedPart.Key}.");
                    }
                }
            }

            if (registration.IsDestinationPart &&
                State.DestinationPart != null &&
                !partAlreadyRegistered)
            {
                return RejectRegistration(
                    $"Cannot register destination part {registration.PartNumber} at '{registration.FilePath}': destination '{State.DestinationPart}' is already registered.");
            }

            var destinationPart = registration.IsDestinationPart
                ? registration.FilePath
                : State.DestinationPart;
            var completedCount = State.CompletedParts.Count + (partAlreadyRegistered ? 0 : 1);
            var hasAllExpectedParts = completedCount == expectedTotalParts &&
                                      HasAllExpectedParts(registration, partAlreadyRegistered, expectedTotalParts);

            if (hasAllExpectedParts && destinationPart == null)
            {
                return RejectRegistration(
                    $"Cannot complete job tracker registration after part {registration.PartNumber}: no destination part was registered.");
            }

            State.TotalParts = expectedTotalParts;
            State.CompletedParts[registration.PartNumber] = registration.FilePath;
            State.DestinationPart = destinationPart;

            // Single-writer completion claim: only the first caller that observes
            // all parts present ever gets IsComplete=true.
            var isComplete = !State.CompletionClaimed && hasAllExpectedParts;
            if (isComplete)
            {
                State.CompletionClaimed = true;
            }

            return Task.FromResult(CreateCompletionResult(isComplete));
        }

        private Task<JobTrackerCompletionResult> RejectRegistration(string reason)
        {
            // Persist the rejection in the same entity operation that observes the contradiction.
            State.RegistrationRejected = true;
            State.RegistrationRejectionReason = reason;
            return Task.FromResult(CreateCompletionResult(false));
        }

        private static string GetRegistrationRejectionReason(JobTrackerRegistration registration)
        {
            if (registration == null)
            {
                return "Job tracker registration is required.";
            }

            if (registration.TotalParts <= 0)
            {
                return $"Invalid job tracker registration: TotalParts={registration.TotalParts} must be positive.";
            }

            if (registration.PartNumber <= 0 || registration.PartNumber > registration.TotalParts)
            {
                return $"Invalid job tracker registration: PartNumber={registration.PartNumber} must be between 1 and TotalParts={registration.TotalParts}.";
            }

            if (string.IsNullOrWhiteSpace(registration.FilePath))
            {
                return $"Invalid job tracker registration: part {registration.PartNumber} requires a file path.";
            }

            return null;
        }

        private string GetStateRejectionReason(int expectedTotalParts)
        {
            if (State.RegistrationRejectionReason != null)
            {
                return "Job tracker has a rejection reason without a rejected registration.";
            }

            if (State.TotalParts < 0)
            {
                return $"Job tracker has invalid TotalParts={State.TotalParts}.";
            }

            if (State.CompletedParts == null)
            {
                return "Job tracker has no completed-parts collection.";
            }

            if (State.TotalParts == 0)
            {
                if (State.CompletedParts.Count != 0 ||
                    State.DestinationPart != null ||
                    State.CompletionClaimed)
                {
                    return "Job tracker has lifecycle data without an initialized total-parts value.";
                }

                return null;
            }

            var registeredPaths = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var completedPart in State.CompletedParts)
            {
                if (completedPart.Key <= 0 || completedPart.Key > expectedTotalParts)
                {
                    return $"Job tracker contains out-of-range part {completedPart.Key}; expected parts 1 through {expectedTotalParts}.";
                }

                if (string.IsNullOrWhiteSpace(completedPart.Value))
                {
                    return $"Job tracker part {completedPart.Key} has no file path.";
                }

                if (registeredPaths.TryGetValue(completedPart.Value, out var existingPartNumber))
                {
                    return $"Job tracker parts {existingPartNumber} and {completedPart.Key} share file path '{completedPart.Value}'.";
                }

                registeredPaths.Add(completedPart.Value, completedPart.Key);
            }

            if (State.DestinationPart != null &&
                !State.CompletedParts.ContainsValue(State.DestinationPart))
            {
                return $"Job tracker destination '{State.DestinationPart}' does not match a registered part.";
            }

            var hasAllExpectedParts = State.CompletedParts.Count == expectedTotalParts;
            if (hasAllExpectedParts && State.DestinationPart == null)
            {
                return "Job tracker contains all expected parts without a destination part.";
            }

            if (State.CompletionClaimed != hasAllExpectedParts)
            {
                return $"Job tracker completion claim is inconsistent with {State.CompletedParts.Count} of {expectedTotalParts} registered parts.";
            }

            return null;
        }

        private bool HasAllExpectedParts(
            JobTrackerRegistration registration,
            bool partAlreadyRegistered,
            int expectedTotalParts)
        {
            for (var partNumber = 1; partNumber <= expectedTotalParts; partNumber++)
            {
                if (!State.CompletedParts.ContainsKey(partNumber) &&
                    (partAlreadyRegistered || partNumber != registration.PartNumber))
                {
                    return false;
                }
            }

            return true;
        }

        private JobTrackerCompletionResult CreateCompletionResult(bool isComplete)
        {
            return new JobTrackerCompletionResult
            {
                TotalParts = State.TotalParts,
                CompletedCount = State.CompletedParts?.Count ?? 0,
                IsComplete = isComplete,
                RegistrationRejected = State.RegistrationRejected,
                RegistrationRejectionReason = State.RegistrationRejectionReason,
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
