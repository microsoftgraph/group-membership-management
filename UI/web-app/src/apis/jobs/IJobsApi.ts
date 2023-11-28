// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Job, NewJob, Page, PagingOptions } from '../../models';

export interface IJobsApi {
  getAllJobs(pagingOptions?: PagingOptions): Promise<Page<Job>>;
  postNewJob(job: NewJob): Promise<Response>;
}
