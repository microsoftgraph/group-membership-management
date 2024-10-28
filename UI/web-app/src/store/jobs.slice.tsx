// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { fetchJobChanges, fetchJobDetails, patchJobDetails, getGroupDetails, removeGMM } from './jobDetails.api';
import { fetchJobs, postJob, getPeoplePickerSuggestions } from './jobs.api';
import type { RootState } from './store';
import { type Job } from '../models/Job';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';
import { PatchJobResponse, RemoveGMMResponse, SyncJobChange } from '../models';

// Define a type for the slice state
export interface JobsState {
  jobsLoading: boolean;
  jobs?: Job[];
  totalNumberOfPages?: number;
  selectedJob?: Job;
  selectedJobLoading: boolean;
  getJobsError: string | undefined;
  getJobDetailsError: string | undefined;
  patchJobDetailsResponse: PatchJobResponse | undefined;
  patchJobDetailsError: string | undefined;
  postJobLoading: boolean;
  postJobError: string | undefined;
  jobOwnerFilterSuggestions?: PeoplePickerPersona[];
  removeGMMLoading: boolean;
  removeGMMResponse: RemoveGMMResponse | undefined;
  removeGMMError: string | undefined;
  selectedJobChanges: SyncJobChange[] | undefined;
  selectedJobChangesLoading: boolean;
  selectedJobChangesError: string | undefined;
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
  jobOwnerFilterSuggestions: [],
  removeGMMLoading: false,
  removeGMMResponse: undefined,
  removeGMMError: undefined,
  selectedJobChanges: undefined,
  selectedJobChangesLoading: false,
  selectedJobChangesError: undefined,
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

    // getGroupDetails
    builder.addCase(getGroupDetails.pending, (state) => {
      state.selectedJobLoading = true;
      state.selectedJob = undefined;
    });
    builder.addCase(getGroupDetails.fulfilled, (state, action) => {
      state.selectedJobLoading = false;
      state.selectedJob = action.payload;
      console.log("state.selectedJob", state.selectedJob);
    });
    builder.addCase(getGroupDetails.rejected, (state, action) => {
      state.getJobDetailsError = action.error.message;
    });

    // patchJobDetails
    builder.addCase(patchJobDetails.pending, (state) => {
      state.patchJobDetailsResponse = undefined;
      state.patchJobDetailsError = undefined;
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
    builder.addCase(getPeoplePickerSuggestions.fulfilled, (state, {payload}: PayloadAction<PeoplePickerPersona[]>) => {
      state.jobOwnerFilterSuggestions = payload;
    });

    // removeGMM
    builder.addCase(removeGMM.pending, (state) => {
      state.removeGMMLoading = true;
      state.removeGMMResponse = undefined;
      state.removeGMMError = undefined;
    });
    builder.addCase(removeGMM.fulfilled, (state, action) => {
      state.removeGMMLoading = false;
      state.removeGMMResponse = action.payload;
    });
    builder.addCase(removeGMM.rejected, (state, action) => {
      state.removeGMMLoading = false;
      state.removeGMMError = action.error.message;
    });

    // fetchJobChanges
    builder.addCase(fetchJobChanges.pending, (state) => {
      state.selectedJobChangesLoading = true;
      state.selectedJobChanges = undefined;
    });
    builder.addCase(fetchJobChanges.fulfilled, (state, action) => {
      state.selectedJobChangesLoading = false;
      state.selectedJobChanges = action.payload;
    });
    builder.addCase(fetchJobChanges.rejected, (state, action) => {
      state.selectedJobChangesError = action.error.message;
    });
  }
});


export const { setJobs, setGetJobsError, setGetJobDetailsError } =
  jobsSlice.actions;

export const selectAllJobs = (state: RootState) => state.jobs.jobs;
export const selectJobsLoading = (state: RootState) => state.jobs.jobsLoading;

export const selectSelectedJobDetails = (state: RootState) =>
  state.jobs.selectedJob;

export const selectSelectedJobLoading = (state: RootState) =>
  state.jobs.selectedJobLoading;

export const selectGetJobsError = (state: RootState) => state.jobs.getJobsError;

export const selectGetJobDetailsError = (state: RootState) =>
  state.jobs.getJobDetailsError;

export const selectSelectedJobChanges = (state: RootState) =>
  state.jobs.selectedJobChanges;
export const selectSelectedJobChangesLoading = (state: RootState) =>
  state.jobs.selectedJobChangesLoading;
export const selectSelectedJobChangesError = (state: RootState) =>
  state.jobs.selectedJobChangesError;

export const getTotalNumberOfPages = (state: RootState) => state.jobs.totalNumberOfPages;

export const selectPatchJobDetailsResponse = (state: RootState) => state.jobs.patchJobDetailsResponse;
export const selectPatchJobDetailsError = (state: RootState) => state.jobs.patchJobDetailsError;

export const selectPostJobLoading = (state: RootState) => state.jobs.postJobLoading;
export const selectPostJobError = (state: RootState) => state.jobs.postJobError;
export const selectPeoplePickerSuggestions = (state: RootState) => state.jobs.jobOwnerFilterSuggestions;

export const selectRemoveGMMLoading = (state: RootState) => state.jobs.removeGMMLoading;
export const selectRemoveGMMResponse = (state: RootState) => state.jobs.removeGMMResponse;
export const selectRemoveGMMError = (state: RootState) => state.jobs.removeGMMError;

export default jobsSlice.reducer;
