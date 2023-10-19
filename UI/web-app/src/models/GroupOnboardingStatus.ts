// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type GroupOnboardingStatus = {
    status: OnboardingStatus;
};

export enum OnboardingStatus {
    Onboarded = '0',
    ReadyForOnboarding = '1',
    NotReadyForOnboarding = '2'
}