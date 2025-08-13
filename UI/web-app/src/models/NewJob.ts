// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { GroupSettings } from "./GroupSettings";
import { SyncJobQuery } from "./SyncJobQuery";
import { Title } from "./Title";

export type NewJob = {
    requestor: string;
    startDate: string;
    period: number;
    query: SyncJobQuery;
    titles: Title[];
    thresholdPercentageForAdditions: number;
    thresholdPercentageForRemovals: number;
    status: string;
    destination: string;
    businessJustification: string;
    lastModifiedOnBehalfOfDisplayName?: string;
    lastModifiedOnBehalfOfObjectId?: string;
    groupSettings?: GroupSettings | undefined;
};
