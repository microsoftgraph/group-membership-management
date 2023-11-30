// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { Job, NewJob, Page, PagingOptions } from '../../models';

export interface IJobsApi {
  getAllJobs(pagingOptions?: PagingOptions): Promise<Page<Job>>;
  postNewJob(job: NewJob): Promise<AxiosResponse>;
}
