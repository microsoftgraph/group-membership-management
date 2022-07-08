// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Services.Entities
{
    public enum MembershipDeltaStatus
    {
        Ok = 0,
        Error = 1,
        ThresholdExceeded = 2,
        DryRun = 3
    }
}
