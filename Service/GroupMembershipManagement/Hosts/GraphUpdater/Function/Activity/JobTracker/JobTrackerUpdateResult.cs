// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace GraphUpdater.Activity.JobTracker
{
    /// <summary>
    /// The outcome of an atomic register-and-check: whether this caller claimed completion,
    /// the observed progress, and a snapshot of the accumulated <see cref="JobState"/>.
    /// </summary>
    public class JobTrackerUpdateResult
    {
        public bool IsComplete { get; set; }
        public int MessagesProcessed { get; set; }
        public int TotalMessageCount { get; set; }
        public JobState State { get; set; }
    }
}
