// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace GraphUpdater.Activity.JobTracker
{
    /// <summary>
    /// The per-message contribution folded into the run's running totals by
    /// <see cref="IJobTracker.RegisterMessageAndCheckComplete"/>.
    /// </summary>
    public class JobTrackerMessageRegistration
    {
        public int MessageIndex { get; set; }
        public int TotalMessageCount { get; set; }
        public int MembersToAdd { get; set; }
        public int MembersToRemove { get; set; }
        public int MembersAdded { get; set; }
        public int MembersRemoved { get; set; }
        public int MembersToAddNotFound { get; set; }
        public int MembersToAddAlreadyExist { get; set; }
        public int MembersToRemoveNotFound { get; set; }
    }
}
