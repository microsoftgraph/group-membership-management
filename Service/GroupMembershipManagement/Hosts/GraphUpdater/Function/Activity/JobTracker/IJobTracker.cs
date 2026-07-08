// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;

namespace GraphUpdater.Activity.JobTracker
{
    public interface IJobTracker
    {
        Task<JobState> GetState();

        /// <summary>
        /// Reads only the destination-group validity (null until established); never rewrites JobState.
        /// </summary>
        Task<bool?> GetIsValidGroup();

        /// <summary>
        /// First-writer-wins set of the destination-group validity, mutating only that field. Cannot
        /// clobber the accumulated message totals (the hazard of the old GetState/SetState pair).
        /// </summary>
        Task<bool> SetIsValidGroupIfUnset(bool isValidGroup);

        /// <summary>
        /// Single-writer claim of the completion send: atomically sets CompletionSent and returns true
        /// only to the first caller (false thereafter), so the signal is sent at most once.
        /// </summary>
        Task<bool> TryMarkCompletionSent();

        /// <summary>
        /// Atomically records this message's results and reports whether the run is complete. Replaces
        /// the split GetState/increment/SetState that lost accumulated state across an idle timeout.
        /// </summary>
        Task<JobTrackerUpdateResult> RegisterMessageAndCheckComplete(JobTrackerMessageRegistration registration);
    }
}
