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
import { format } from '@fluentui/react';
import { strings } from '../services/localization/i18n/locales/en/translations';

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
        const lastRunTime = formatLastRunTime(index['lastSuccessfulRunTime']);
        const estimatedNextRunTime = formatNextRunTime(index['estimatedNextRunTime'], index['enabledOrNot']);
        const SQLMinDate = new Date(Date.UTC(1753, 0, 1));
        
        if(lastRunTime[0] === SQLMinDate.toLocaleDateString()) { // Jobs that haven't run yet
          index['lastSuccessfulRunTime'] = "Pending initial sync";
          index['estimatedNextRunTime'] = "Pending initial sync";
        }
        else {
          index['lastSuccessfulRunTime'] = `${format(strings.hoursAgo, lastRunTime[0], lastRunTime[1])}`;
          index['estimatedNextRunTime'] = estimatedNextRunTime[0] === "-" ? "-" // Disabled jobs
                                            : `${format(strings.hoursLeft, estimatedNextRunTime[0], estimatedNextRunTime[1])}`;
        }
        
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
