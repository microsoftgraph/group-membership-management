// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SyncJobQuery } from "./SyncJobQuery";

export type NewJob = {
    requestor: string;
    startDate: string;
    period: number;
    query: SyncJobQuery;
    thresholdPercentageForAdditions: number;
    thresholdPercentageForRemovals: number;
    status: string;
    destination: string;
};
