// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { PagingOptions } from './PagingOptions';

export type GetJobChangesRequest = {
    syncJobId: string;
    pagingOptions?: PagingOptions;
};