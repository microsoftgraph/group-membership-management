// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { type Job } from '../models/Job';
import { ThunkConfig } from './store';
import { 
  NewJob, 
  PostJobResponse, 
  Page, 
  PagingOptions,
  PeoplePickerPersona
} from '../models';
import { processJob } from '../utils/jobUtils';

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
      const mapped = jobsPage.items.map(processJob);
      jobsPage.items = mapped;
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

export const getPeoplePickerSuggestions = createAsyncThunk<PeoplePickerPersona[], {displayName: string; alias: string}, ThunkConfig>(
  'filter/getPeoplePickerSuggestions',
  async (input, { extra }) => {
    const { graphApi } = extra.apis;
    try {
      return await graphApi.getPeoplePickerSuggestions(input.displayName, input.alias);
    } catch (error) {
      throw new Error('Failed to call getPeoplePickerSuggestions endpoint');
    }
  }
);
