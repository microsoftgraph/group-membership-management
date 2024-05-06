// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { ApiBase } from '../ApiBase';
import { IJobsApi } from './IJobsApi';
import { JobEntity } from '../entities';
import { Page, Job, PagingOptions, NewJob } from '../../models';
import { ODataQueryOptions } from '../entities';

export class JobsApi extends ApiBase implements IJobsApi {

  public async getAllJobs(pagingOptions?: PagingOptions): Promise<Page<Job>> {

    const params = this.mapPagingOptionsToODataQueryOptions(pagingOptions);
    const response = await this.httpClient.get<JobEntity[]>('/', { params });

    this.ensureSuccessStatusCode(response);

    const xTotalPages = response.headers['x-total-pages'];
    const jobsPage: Page<Job> = {
      items: response.data.map((entity) => this.mapJobEntityToJob(entity)),
      totalNumberOfPages: xTotalPages ? parseInt(xTotalPages) : 1,
    };

    return jobsPage;
  }

  public async postNewJob(job: NewJob): Promise<AxiosResponse> {
    const jobWithSerializedQuery = {
      ...job,
      query: JSON.stringify(job.query),
    };
    const response = await this.httpClient.post('/', jobWithSerializedQuery);
    this.ensureSuccessStatusCode(response);
    return response;
}

  private mapPagingOptionsToODataQueryOptions(pagingOptions?: PagingOptions): ODataQueryOptions | undefined {
    return pagingOptions
      ? {
          $skip: pagingOptions.itemsToSkip,
          $top: pagingOptions.pageSize,
          $orderBy: pagingOptions.orderBy,
          $filter: pagingOptions.filter,
        }
      : undefined;
  }

  private mapJobEntityToJob(entity: JobEntity): Job {
    return {
      syncJobId: entity.syncJobId,
      targetGroupId: entity.targetGroupId,
      targetGroupType: entity.targetGroupType,
      targetGroupName: entity.targetGroupName,
      startDate: entity.startDate,
      lastSuccessfulStartTime: entity.lastSuccessfulStartTime,
      lastSuccessfulRunTime: entity.lastSuccessfulRunTime,
      query: entity.query,
      actionRequired: entity.actionRequired,
      enabledOrNot: entity.enabledOrNot,
      status: entity.status,
      period: entity.period,
      arrow: entity.arrow,
      estimatedNextRunTime: entity.estimatedNextRunTime,
      thresholdPercentageForAdditions: entity.thresholdPercentageForAdditions,
      thresholdPercentageForRemovals: entity.thresholdPercentageForRemovals,
      endpoints: entity.endpoints,
    };
  }
}
