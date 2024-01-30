// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type GroupOnboardingStatus = {
    status: OnboardingStatus;
};

export enum OnboardingStatus {
    Onboarded,
    ReadyForOnboarding,
    NotReadyForOnboarding
}