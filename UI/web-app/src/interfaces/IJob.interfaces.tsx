// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface IJob {
    partitionKey: string;
    rowKey: string;
    targetGroupId: string;
    targetGroupType: string;
    startDate: string;
    lastSuccessfulStartTime: string;
    lastSuccessfulRunTime: string;
    estimatedNextRunTime: string;
    thresholdPercentageForAdditions: number;
    thresholdPercentageForRemovals: number;
}