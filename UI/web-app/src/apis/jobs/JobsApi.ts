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
    const response = await this.httpClient.get<{
      items: JobEntity[];
      totalNumberOfPages: number;
      currentPage: number;
      pageSize: number;
      totalItems: number;
    }>('/', { params });

    this.ensureSuccessStatusCode(response);

    const jobsPage: Page<Job> = {
      items: response.data.items.map((entity) => this.mapJobEntityToJob(entity)),
      totalNumberOfPages: response.data.totalNumberOfPages,
      currentPage: response.data.currentPage,
      pageSize: response.data.pageSize,
      totalItems: response.data.totalItems,
    };

    return jobsPage;
  }

  public async downloadJobs(syncJobIds: string[]): Promise<AxiosResponse> {
    const response = await this.httpClient.post('/bulkDownload', syncJobIds);
    this.ensureSuccessStatusCode(response);
    return response;
  }

   public async approveJobs(syncJobIds: string[]): Promise<AxiosResponse> {
    const response = await this.httpClient.post('/bulkApprove', syncJobIds);
    this.ensureSuccessStatusCode(response);
    return response;
  }

  public async postNewJob(job: NewJob): Promise<AxiosResponse> {
    const jobWithSerializedQuery = {
      ...job,
      titles: job.titles,
      query: JSON.stringify(job.query),
    };
    const response = await this.httpClient.post('/', jobWithSerializedQuery);
    this.ensureSuccessStatusCode(response);
    return response;
  }

  private mapPagingOptionsToODataQueryOptions(pagingOptions?: PagingOptions): ODataQueryOptions | undefined {
    const capitalizeFirstLetter = (input: string): string => {
      return input.charAt(0).toUpperCase() + input.slice(1);
    };

    const queryOptions: ODataQueryOptions = pagingOptions
      ? {
          $skip: pagingOptions.itemsToSkip,
          $top: pagingOptions.pageSize,
          $orderBy: pagingOptions.orderBy ? capitalizeFirstLetter(pagingOptions.orderBy) : undefined,
          $filter: pagingOptions.filter,
          customSortBy: pagingOptions.customSortBy 
        }
      : {};

    return queryOptions;
  }

  private mapJobEntityToJob(entity: JobEntity): Job {
    return {
      syncJobId: entity.syncJobId,
      targetGroupId: entity.targetGroupId,
      targetChannelId: entity.targetChannelId,
      targetDestinationType: entity.targetDestinationType,
      targetGroupName: entity.targetGroupName,
      targetChannelName: entity.targetChannelName,
      email: entity.targetGroupEmail,
      startDate: entity.startDate,
      lastSuccessfulStartTime: entity.lastSuccessfulStartTime,
      lastSuccessfulRunTime: entity.lastSuccessfulRunTime,
      query: entity.query,
      titles: entity.titles,
      actionRequired: entity.actionRequired,
      enabledOrNot: entity.enabledOrNot,
      status: entity.status,
      period: entity.period,
      arrow: entity.arrow,
      estimatedNextRunTime: entity.estimatedNextRunTime,
      thresholdPercentageForAdditions: entity.thresholdPercentageForAdditions,
      thresholdPercentageForRemovals: entity.thresholdPercentageForRemovals,
      endpoints: entity.endpoints,
      requestor: entity.requestor,
    };
  }
}
