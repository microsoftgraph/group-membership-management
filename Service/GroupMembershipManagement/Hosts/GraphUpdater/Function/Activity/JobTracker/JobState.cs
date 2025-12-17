// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace GraphUpdater.Activity.JobTracker
{
    public class JobState
    {
        public bool? IsValidGroup { get; set; }
        public bool CompletionSent { get; set; }
        public int TotalMembersToAdd { get; set; }
        public int TotalMembersToRemove { get; set; }
        public int TotalMembersAdded { get; set; }
        public int TotalMembersRemoved { get; set; }
        public int TotalMembersToAddNotFound { get; set; }
        public int TotalMembersToAddAlreadyExist { get; set; }
        public int TotalMembersToRemoveNotFound { get; set; }
        public int MessagesProcessed { get; set; }
    }
}
