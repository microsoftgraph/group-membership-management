// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Entities
{
    public enum SyncStatus
    {
        All = 0,
        InProgress = 1,
        Idle = 2,
        Error = 3,
        ThresholdExceeded = 4,
        NoSourceMembership = 5
    }
}