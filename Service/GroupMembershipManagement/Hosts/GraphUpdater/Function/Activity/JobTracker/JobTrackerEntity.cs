// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GraphUpdater.Activity.JobTracker
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

        public Task<JobState> GetState()
        {
            return Task.FromResult(State);
        }

        /// <summary>
        /// Reads only the destination-group validity, without materializing or rewriting the whole
        /// <see cref="JobState"/>. Returns null when validity has not yet been established.
        /// </summary>
        public Task<bool?> GetIsValidGroup()
        {
            return Task.FromResult(State.IsValidGroup);
        }

        /// <summary>
        /// First-writer-wins set of the destination-group validity, mutating only that field. Never
        /// rewrites the whole <see cref="JobState"/>, so it cannot clobber the accumulated totals.
        /// </summary>
        public Task<bool> SetIsValidGroupIfUnset(bool isValidGroup)
        {
            State.IsValidGroup ??= isValidGroup;
            return Task.FromResult(State.IsValidGroup.Value);
        }

        /// <summary>
        /// Single-writer claim of the completion send: atomically sets CompletionSent and returns true
        /// only to the first caller (false thereafter), so the completion signal is sent at most once.
        /// </summary>
        public Task<bool> TryMarkCompletionSent()
        {
            if (State.CompletionSent)
            {
                return Task.FromResult(false);
            }

            State.CompletionSent = true;
            return Task.FromResult(true);
        }

        /// <summary>
        /// Atomic register-and-check: folds one message's results into the run totals and reports
        /// whether the run is complete. A single entity operation (read + accumulate + decide) removes
        /// the window in which the old split GetState/SetState lost accumulated state during a stall.
        /// </summary>
        public Task<JobTrackerUpdateResult> RegisterMessageAndCheckComplete(JobTrackerMessageRegistration registration)
        {
            State.ProcessedMessageIndices ??= new HashSet<int>();

            if (registration == null)
            {
                return Task.FromResult(BuildResult(isComplete: false));
            }

            // Reject a self-inconsistent registration (non-positive total/index, or index beyond its
            // own declared total): folding it in could claim completion while a genuine part is missing.
            if (registration.TotalMessageCount <= 0
                || registration.MessageIndex <= 0
                || registration.MessageIndex > registration.TotalMessageCount)
            {
                return Task.FromResult(BuildResult(isComplete: false));
            }

            // First-writer-wins for the expected message count: a later message that disagrees
            // must not overwrite the entity's view.
            if (State.TotalMessageCount == 0)
            {
                State.TotalMessageCount = registration.TotalMessageCount;
            }

            // Reject an index beyond the already-established run size (a message disagreeing on the
            // total): folding it in could claim completion with a genuine part absent.
            if (registration.MessageIndex > State.TotalMessageCount)
            {
                return Task.FromResult(BuildResult(isComplete: false));
            }

            // Idempotent accumulation keyed by MessageIndex: a retried, replayed, or redelivered
            // message is counted exactly once.
            if (State.ProcessedMessageIndices.Add(registration.MessageIndex))
            {
                State.TotalMembersToAdd += registration.MembersToAdd;
                State.TotalMembersToRemove += registration.MembersToRemove;
                State.TotalMembersAdded += registration.MembersAdded;
                State.TotalMembersRemoved += registration.MembersRemoved;
                State.TotalMembersToAddNotFound += registration.MembersToAddNotFound;
                State.TotalMembersToAddAlreadyExist += registration.MembersToAddAlreadyExist;
                State.TotalMembersToRemoveNotFound += registration.MembersToRemoveNotFound;
                State.MessagesProcessed = State.ProcessedMessageIndices.Count;
            }

            // Indices are validated to 1..TotalMessageCount and deduped, so equality holds exactly
            // when every distinct part has arrived — never tripped early by a bad or duplicate index.
            var allProcessed = State.TotalMessageCount > 0 && State.MessagesProcessed == State.TotalMessageCount;

            // Single-writer completion claim: only the first caller that observes every message
            // processed ever gets IsComplete=true, so the job is finalized exactly once.
            var isComplete = false;
            if (allProcessed && !State.CompletionClaimed)
            {
                State.CompletionClaimed = true;
                isComplete = true;
            }

            return Task.FromResult(BuildResult(isComplete));
        }

        private JobTrackerUpdateResult BuildResult(bool isComplete)
        {
            return new JobTrackerUpdateResult
            {
                IsComplete = isComplete,
                MessagesProcessed = State.MessagesProcessed,
                TotalMessageCount = State.TotalMessageCount,
                State = Clone(State)
            };
        }

        private static JobState Clone(JobState source)
        {
            return new JobState
            {
                IsValidGroup = source.IsValidGroup,
                CompletionSent = source.CompletionSent,
                CompletionClaimed = source.CompletionClaimed,
                TotalMessageCount = source.TotalMessageCount,
                TotalMembersToAdd = source.TotalMembersToAdd,
                TotalMembersToRemove = source.TotalMembersToRemove,
                TotalMembersAdded = source.TotalMembersAdded,
                TotalMembersRemoved = source.TotalMembersRemoved,
                TotalMembersToAddNotFound = source.TotalMembersToAddNotFound,
                TotalMembersToAddAlreadyExist = source.TotalMembersToAddAlreadyExist,
                TotalMembersToRemoveNotFound = source.TotalMembersToRemoveNotFound,
                MessagesProcessed = source.MessagesProcessed,
                ProcessedMessageIndices = new HashSet<int>(source.ProcessedMessageIndices ?? new HashSet<int>())
            };
        }

        [Function(nameof(JobTrackerEntity))]
        public static Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync<JobTrackerEntity>();
        }
    }
}
