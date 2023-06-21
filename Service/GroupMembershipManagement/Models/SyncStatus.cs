// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public enum SyncStatus
    {
        All = 0,
        InProgress = 1,
        Idle = 2,
        Error = 3,
        ThresholdExceeded = 4,
        MembershipDataNotFound = 5,
        DestinationGroupNotFound = 6,
        NotOwnerOfDestinationGroup = 7,
        SecurityGroupNotFound = 8,
        FilePathNotValid = 9,
        QueryNotValid = 10,
        FileNotFound = 11,
        CustomerPaused = 12,
        StuckInProgress = 13,
        ErroredDueToStuckInProgress = 14,
        PrivateChannelNotDestination = 15,
        DestinationQueryNotValid = 16,
    }
}