// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.MessageSplitter
{
    public class InstanceTrackerOrchestratorRequest
    {
        public string UpdaterType { get; }
        public string CurrentLaneSize { get; }

        public InstanceTrackerOrchestratorRequest(string updaterType, string currentLaneSize)
        {
            UpdaterType = updaterType;
            CurrentLaneSize = currentLaneSize;
        }
    }
}
