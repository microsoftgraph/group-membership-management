// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { PagingOptions } from "./PagingOptions";

export type GetJobsRequest = {
    pagingOptions?: PagingOptions;
    name?: string;
    owner?: string;
};