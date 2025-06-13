// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { fetchJobChanges, fetchJobDetails, patchJobDetails, getGroupDetails, removeGMM, getChannelDetails } from './jobDetails.api';
import { fetchJobs, postJob, getPeoplePickerSuggestions, downloadJobs, approveJobs } from './jobs.api';
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
  jobsToDownload?: Job[];
  downloadJobsLoading: boolean;
  downloadJobsError: string | undefined;
  approveJobsLoading: boolean;
  approveJobsResponse: string | undefined;
  approveJobsError: string | undefined;
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
  jobsToDownload: undefined,
  downloadJobsLoading: false,
  downloadJobsError: undefined,
  approveJobsLoading: false,
  approveJobsResponse: undefined,
  approveJobsError: undefined,
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
    clearJob: (state) => {
      state.selectedJob = undefined;
    },
    clearJobsToDownload: (state) => {
      state.jobsToDownload = undefined;
    },
    updateJobOwnerFilterSuggestions: (state) => {
      state.jobOwnerFilterSuggestions = [];
    },
    setApproveJobsLoading: (state) => {
      state.approveJobsLoading = false;
    },
    setApproveJobsResponse: (state) => {
      state.approveJobsResponse = undefined;
    }
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
      state.selectedJobLoading = false;
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
    });
    builder.addCase(getGroupDetails.rejected, (state, action) => {
      state.selectedJobLoading = false;
      state.getJobDetailsError = action.error.message;
    });

    // getChannelDetails
    builder.addCase(getChannelDetails.pending, (state) => {
      state.selectedJobLoading = true;
      state.selectedJob = undefined;
    });
    builder.addCase(getChannelDetails.fulfilled, (state, action) => {
      state.selectedJobLoading = false;
      state.selectedJob = action.payload;
    });
    builder.addCase(getChannelDetails.rejected, (state, action) => {
      state.selectedJobLoading = false;
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

    // downloadJobs
    builder.addCase(downloadJobs.pending, (state) => {
      state.downloadJobsLoading = true;
      state.downloadJobsError = undefined;
    });
    builder.addCase(downloadJobs.fulfilled, (state, action) => {
      state.downloadJobsLoading = false;
      state.jobsToDownload = action.payload;
    });
    builder.addCase(downloadJobs.rejected, (state, action) => {
      state.downloadJobsLoading = false;
      state.downloadJobsError = action.error.message;
    });

    // approveJobs
    builder.addCase(approveJobs.pending, (state) => {
      state.approveJobsLoading = true;
      state.approveJobsResponse = undefined;
      state.approveJobsError = undefined;
    });
    builder.addCase(approveJobs.fulfilled, (state, action) => {
      state.approveJobsLoading = false;
      state.approveJobsResponse = action.payload;
      console.log('approveJobs fulfilled', action.payload);
    });
    builder.addCase(approveJobs.rejected, (state, action) => {
      state.approveJobsLoading = false;
      state.approveJobsError = action.error.message;
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


export const { setJobs, setGetJobsError, setGetJobDetailsError, clearJob, clearJobsToDownload, updateJobOwnerFilterSuggestions, setApproveJobsLoading, setApproveJobsResponse } =
  jobsSlice.actions;

export const selectAllJobs = (state: RootState) => state.jobs.jobs;
export const selectJobsLoading = (state: RootState) => state.jobs.jobsLoading;

export const selectSelectedJobDetails = (state: RootState) =>
  state.jobs.selectedJob;

export const downloadJobsLoading = (state: RootState) => state.jobs.downloadJobsLoading;
export const selectJobsToDownload = (state: RootState) => state.jobs.jobsToDownload;
export const downloadJobsError = (state: RootState) => state.jobs.downloadJobsError;

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

export const selectApproveJobsLoading = (state: RootState) => state.jobs.approveJobsLoading;
export const selectApproveJobsResponse = (state: RootState) => state.jobs.approveJobsResponse;
export const selectApproveJobsrror = (state: RootState) => state.jobs.approveJobsError;

export default jobsSlice.reducer;
