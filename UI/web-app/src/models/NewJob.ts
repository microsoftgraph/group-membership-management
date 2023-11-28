// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type NewJob = {
    requestor: string;
    startDate: string;
    period: number;
    query: string;
    thresholdPercentageForAdditions: number;
    thresholdPercentageForRemovals: number;
    status: string;
    destination: string;
};
