// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Services.Entities
{
    public enum GraphUpdaterStatus
    {
        Ok = 0,
        Error = 1,
        ThresholdExceeded = 2,
        GuestError = 3
    }
}
