// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
export interface IJob {
    Identifier: string;
    ObjectType: string;
    Status: boolean;
    ThresholdIncrease: number;
    ThresholdDecrease: number;
    LastAttemptedRunTime: string;
    LastSuccessfulRuntime: string;
    EstimatedNextRuntime: string;
    InitialOnboardingTime: string;
}