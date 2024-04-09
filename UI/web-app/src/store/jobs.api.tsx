// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { SyncStatus, ActionRequired } from '../models/Status';
import { type Job } from '../models/Job';
import { ThunkConfig } from './store';
import { 
  NewJob, 
  PostJobResponse, 
  Page, 
  PagingOptions,
  PeoplePickerPersona
} from '../models';
import { formatLastRunTime, formatNextRunTime } from '../utils/dateUtils';

export interface JobsResponse {
  jobs: Job[];
  totalNumberOfPages: number;
}

export const fetchJobs = createAsyncThunk<Page<Job>, PagingOptions | undefined, ThunkConfig>(
  'jobs/fetchJobs',
  async (pagingOptions, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      const jobsPage = await gmmApi.jobs.getAllJobs(pagingOptions);

      const mapped = jobsPage.items.map((index) => {
        index['enabledOrNot'] =
        index['status'] === SyncStatus.Idle || index['status'] === SyncStatus.InProgress ? true : false;
        index['lastSuccessfulRunTime'] = formatLastRunTime(index['lastSuccessfulRunTime'], index['period']).join(' ');
        index['estimatedNextRunTime'] = formatNextRunTime(index['estimatedNextRunTime'], index['period'], index['enabledOrNot']).join(' ');
        index['arrow'] = '';

        return index;
      });

      const newPayload = mapped.map((index) => {
        switch (index['status']) {
          case SyncStatus.ThresholdExceeded:
            index['actionRequired'] = ActionRequired.ThresholdExceeded;
            break;
          case SyncStatus.CustomerPaused:
            index['actionRequired'] = ActionRequired.CustomerPaused;
            break;
          case SyncStatus.MembershipDataNotFound:
            index['actionRequired'] = ActionRequired.MembershipDataNotFound;
            break;
          case SyncStatus.DestinationGroupNotFound:
            index['actionRequired'] = ActionRequired.DestinationGroupNotFound;
            break;
          case SyncStatus.NotOwnerOfDestinationGroup:
            index['actionRequired'] = ActionRequired.NotOwnerOfDestinationGroup;
            break;
          case SyncStatus.SecurityGroupNotFound:
            index['actionRequired'] = ActionRequired.SecurityGroupNotFound;
            break;
          case SyncStatus.PendingReview:
            index['actionRequired'] = ActionRequired.PendingReview;
            break;
          case SyncStatus.SubmissionRejected:
            index['actionRequired'] = ActionRequired.SubmissionRejected;
            break;
        }
        return index;
      });

      jobsPage.items = newPayload;
      return jobsPage;
    } catch (error) {
      throw new Error('Failed to fetch jobs!');
    }
  }
);

export const postJob = createAsyncThunk<PostJobResponse, NewJob, ThunkConfig>(
  'jobs/postJob',
  async (newJob: NewJob, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      const response = await gmmApi.jobs.postNewJob(newJob);
      let postResponse: PostJobResponse = {
        ok: response.status >= 200 && response.status < 300,
        statusCode: response.status,
      };

      if (!postResponse.ok) {
        postResponse.errorCode = response.data?.detail;
        postResponse.newSyncJobId = response.data?.responseData;
      }
      
      return postResponse;
    } catch (error) {
      throw new Error('Failed to post job!');
    }
  }
);

export const getJobOwnerFilterSuggestions = createAsyncThunk<PeoplePickerPersona[], {displayName: string; alias: string}, ThunkConfig>(
  'filter/getJobOwnerFilterSuggestions',
  async (input, { extra }) => {
    const { graphApi } = extra.apis;
    try {
      return await graphApi.getJobOwnerFilterSuggestions(input.displayName, input.alias);
    } catch (error) {
      throw new Error('Failed to call getJobOwnerFilterSuggestions endpoint');
    }
  }
);
