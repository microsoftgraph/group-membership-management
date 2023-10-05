// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.TeamsChannelUpdater
{
    public class SyncCompleteCustomEvent
    {
        public string Destination { get; set; } = "N/A";
        public string RunId { get; set; } = "N/A";
        public string IsDryRunEnabled { get; set; } = "N/A";
        public string ProjectedMemberCount { get; set; } = "N/A";
        public string MembersToAdd { get; set; } = "N/A";
        public string MembersAdded { get; set; } = "N/A";
        public string MembersToAddNotFound { get; set; } = "N/A";
        public string MembersToAddAlreadyExist { get; set; } = "N/A";
        public string MembersToRemove { get; set; } = "N/A";
        public string MembersRemoved { get; set; } = "N/A";
        public string MembersToRemoveNotFound { get; set; } = "N/A";
        public string IsInitialSync { get; set; } = "N/A";
        public string Result { get; set; } = "N/A";
        public string SyncJobTimeElapsedSeconds { get; set; } = "N/A";
        public string Type { get; set; } = "N/A";
        public string SourceTypesCounts { get; set; } = "N/A";
    }
}
