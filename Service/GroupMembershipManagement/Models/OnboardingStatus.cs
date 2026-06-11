// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public enum OnboardingStatus
    {
        Onboarded = 0,
        ReadyForOnboarding = 1,
        GmmNotOwner = 2,
        UserNotOwner = 3,
        SyncedOnPremises = 4
    }
}