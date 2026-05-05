// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    // Common shape for the per-host SyncCompleteCustomEvent classes so they
    // can share a single telemetry-emission path.
    public interface ISyncCompleteCustomEvent
    {
        string Result { get; set; }
        string Type { get; set; }
        string IsDryRunEnabled { get; set; }
        string SyncJobTimeElapsedSeconds { get; set; }
    }
}
