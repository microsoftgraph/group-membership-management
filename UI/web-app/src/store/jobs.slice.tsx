// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { fetchJobDetails, patchJobDetails } from './jobDetails.api';
import { fetchJobs, postJob, getJobOwnerFilterSuggestions } from './jobs.api';
import type { RootState } from './store';
import { type Job } from '../models/Job';
import { type JobDetails } from '../models/JobDetails';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';
import { PatchJobResponse } from '../models';

// Define a type for the slice state
export interface JobsState {
  jobsLoading: boolean;
  jobs?: Job[];
  totalNumberOfPages?: number;
  selectedJob: JobDetails | undefined;
  selectedJobLoading: boolean;
  getJobsError: string | undefined;
  getJobDetailsError: string | undefined;
  patchJobDetailsResponse: PatchJobResponse | undefined;
  patchJobDetailsError: string | undefined;
  postJobLoading: boolean;
  postJobError: string | undefined;
  jobOwnerFilterSuggestions?: PeoplePickerPersona[];
}

// Define the initial state using that type
const initialState: JobsState = {
  jobsLoading: false,
  jobs: undefined,
  selectedJob: undefined,
  selectedJobLoading: false,
  getJobsError: undefined,
  getJobDetailsError: undefined,
  patchJobDetailsResponse: undefined,
  patchJobDetailsError: undefined,
  postJobLoading: false,
  postJobError: undefined,
  jobOwnerFilterSuggestions: []
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
      state.jobs = action.payload.items;
      state.totalNumberOfPages = action.payload.totalNumberOfPages;
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

    // patchJobDetails
    builder.addCase(patchJobDetails.pending, (state) => {
    });
    builder.addCase(patchJobDetails.fulfilled, (state, action) => {
      state.patchJobDetailsResponse = action.payload;
    });
    builder.addCase(patchJobDetails.rejected, (state, action) => {
      state.patchJobDetailsError = action.error.message;
    });

    // postJob 
    builder.addCase(postJob.pending, (state) => {
      state.postJobLoading = true;
      state.postJobError = undefined;
    });
    builder.addCase(postJob.fulfilled, (state, action) => {
      state.postJobLoading = false;
    });
    builder.addCase(postJob.rejected, (state, action) => {
      state.postJobLoading = false;
      state.postJobError = action.error.message;
    });

    // jobOwnerFilterSuggestions
    builder.addCase(getJobOwnerFilterSuggestions.fulfilled, (state, {payload}: PayloadAction<PeoplePickerPersona[]>) => {
      state.jobOwnerFilterSuggestions = payload;
    });
  }
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

export const getTotalNumberOfPages = (state: RootState) => state.jobs.totalNumberOfPages;

export const selectPatchJobDetailsResponse = (state: RootState) => state.jobs.patchJobDetailsResponse;
export const selectPatchJobDetailsError = (state: RootState) => state.jobs.patchJobDetailsError;

export const selectPostJobLoading = (state: RootState) => state.jobs.postJobLoading;
export const selectPostJobError = (state: RootState) => state.jobs.postJobError;
export const selectJobOwnerFilterSuggestions = (state: RootState) => state.jobs.jobOwnerFilterSuggestions;

export default jobsSlice.reducer;
