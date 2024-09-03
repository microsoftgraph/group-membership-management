// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace MembershipAggregator.Services.Entities
{
    public enum MembershipDeltaStatus
    {
        Ok = 0,
        Error = 1,
        ThresholdExceeded = 2,
        DryRun = 3,
        NoChanges = 4
    }
}
