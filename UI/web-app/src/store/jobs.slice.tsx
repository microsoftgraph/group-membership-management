// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { fetchJobDetails } from './jobDetails.api';
import { fetchJobs } from './jobs.api';
import type { RootState } from './store';
import { type Job } from '../models/Job';
import { type JobDetails } from '../models/JobDetails';

// Define a type for the slice state
export interface JobsState {
  jobsLoading: boolean;
  jobs?: Job[];
  selectedJob: JobDetails | undefined;
  selectedJobLoading: boolean;
  getJobsError: string | undefined;
  getJobDetailsError: string | undefined;
}

// Define the initial state using that type
const initialState: JobsState = {
  jobsLoading: false,
  jobs: undefined,
  selectedJob: undefined,
  selectedJobLoading: false,
  getJobsError: undefined,
  getJobDetailsError: undefined,
};

export const jobsSlice = createSlice({
  name: 'jobs',
  initialState,
  reducers: {
    setJobs: (state, action: PayloadAction<Job[]>) => {
      state.jobs = action.payload;
    },
    setGetJobsError: (state) => {
      state.getJobsError = undefined;
    },
    setGetJobDetailsError: (state) => {
      state.getJobDetailsError = undefined;
    },
  },
  extraReducers: (builder) => {
    builder.addCase(fetchJobs.pending, (state) => {
      state.jobsLoading = true;
    });
    builder.addCase(fetchJobs.fulfilled, (state, action) => {
      state.jobsLoading = false;
      state.jobs = action.payload;
    });
    builder.addCase(fetchJobs.rejected, (state, action) => {
      state.jobsLoading = false;
      state.getJobsError = action.error.message;
    });

    // fetchJobDetails
    builder.addCase(fetchJobDetails.pending, (state) => {
      state.selectedJobLoading = true;
      state.selectedJob = undefined;
    });
    builder.addCase(fetchJobDetails.fulfilled, (state, action) => {
      state.selectedJobLoading = false;
      state.selectedJob = action.payload;
    });
    builder.addCase(fetchJobDetails.rejected, (state, action) => {
      state.getJobDetailsError = action.error.message;
    });
  },
});

export const { setJobs, setGetJobsError, setGetJobDetailsError } =
  jobsSlice.actions;

export const selectAllJobs = (state: RootState) => state.jobs.jobs;

export const selectSelectedJobDetails = (state: RootState) =>
  state.jobs.selectedJob;

export const selectSelectedJobLoading = (state: RootState) =>
  state.jobs.selectedJobLoading;

export const selectGetJobsError = (state: RootState) => state.jobs.getJobsError;

export const selectGetJobDetailsError = (state: RootState) =>
  state.jobs.getJobDetailsError;

export default jobsSlice.reducer;
