// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type GroupOnboardingStatus = {
    status: OnboardingStatus;
    additionalDetails?: {[key: string]: string};
};

export enum OnboardingStatus {
    Onboarded,
    ReadyForOnboarding,
    GmmNotOwner,
    UserNotOwner
};