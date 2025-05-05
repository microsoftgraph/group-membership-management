// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.SyncJobChange
{
    public enum SyncJobChangeReason
    {
        Onboarding,
        StatusUpdate,
        Update,
        SubmissionApproved,
        SubmissionRejected,
        IgnoreThresholdOnce,
        GroupSettings
    }
}
