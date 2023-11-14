// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Job, Page, PagingOptions } from '../../models';

export interface IJobsApi {
  getAllJobs(pagingOptions?: PagingOptions): Promise<Page<Job>>;
}
