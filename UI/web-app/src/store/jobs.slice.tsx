// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { fetchJobChanges, fetchJobDetails, patchJobDetails, getGroupDetails, removeGMM, getChannelDetails } from './jobDetails.api';
import { fetchJobs, postJob, getPeoplePickerSuggestions, downloadJobs, approveJobs } from './jobs.api';
import type { RootState } from './store';
import { type Job } from '../models/Job';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';
import { PatchJobResponse, RemoveGMMResponse, SyncJobChange } from '../models';
import { Title } from '../models/Title';

// Define a type for the slice state
export interface JobsState {
  jobsLoading: boolean;
  jobs?: Job[];
  totalNumberOfPages?: number;
  selectedJob?: Job;
  selectedJobLoading: boolean;
  /**
   * Request ID of the most recently dispatched thunk that targets `selectedJob`
   * (fetchJobDetails / getGroupDetails / getChannelDetails). Used to discard
   * late-arriving fulfilled/rejected actions from earlier dispatches when the
   * user navigates between jobs/groups/channels rapidly. Shared because all
   * three thunks write the same `selectedJob` slot.
   */
  selectedJobRequestId?: string;
  getJobsError: string | undefined;
  getJobDetailsError: string | undefined;
  patchJobDetailsResponse: PatchJobResponse | undefined;
  patchJobDetailsError: string | undefined;
  postJobLoading: boolean;
  postJobError: string | undefined;
  jobsToDownload?: Job[];
  downloadJobsLoading: boolean;
  downloadJobsError: string | undefined;
  totalNumberOfApprovedJobs: number | undefined;
  totalNumberOfJobs: number | undefined;
  approveJobsLoading: boolean;
  approveJobsError: string | undefined;
  jobOwnerFilterSuggestions?: PeoplePickerPersona[];
  removeGMMLoading: boolean;
  removeGMMResponse: RemoveGMMResponse | undefined;
  removeGMMError: string | undefined;
  selectedJobChanges: SyncJobChange[] | undefined;
  /** Request ID guard for fetchJobChanges; mirrors selectedJobRequestId but for the changes feed. */
  selectedJobChangesRequestId?: string;
  selectedJobChangesLoading: boolean;
  selectedJobChangesError: string | undefined;
  selectedJobWithNoTitles: boolean;
  generatedTitlesYet: boolean;
  jobIdSet: string;
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
  totalNumberOfApprovedJobs: undefined,
  totalNumberOfJobs: undefined,
  approveJobsLoading: false,
  approveJobsError: undefined,
  jobOwnerFilterSuggestions: [],
  removeGMMLoading: false,
  removeGMMResponse: undefined,
  removeGMMError: undefined,
  selectedJobChanges: undefined,
  selectedJobChangesLoading: false,
  selectedJobChangesError: undefined,
  selectedJobWithNoTitles: false,
  generatedTitlesYet: false,
  jobIdSet: ""
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
      state.totalNumberOfApprovedJobs = undefined;
      state.totalNumberOfJobs = undefined;
    },
    setGeneratedTitlesYet: (state, action: PayloadAction<boolean>) => {
      state.generatedTitlesYet = action.payload;
    },
    setJobId: (state, action: PayloadAction<string>) => {
      state.jobIdSet = action.payload;
    },
    setSelectedJobEnabled: (state, action: PayloadAction<boolean>) => {
      if (state.selectedJob) {
        state.selectedJob.enabledOrNot = action.payload;
      }
    },
    setSelectedJobStatus: (state, action: PayloadAction<string>) => {
      if (state.selectedJob) {
        state.selectedJob.status = action.payload;
      }
    },
    // Optimistically remove a job from the cached list after approve/reject.
    removeJobFromList: (state, action: PayloadAction<string>) => {
      if (state.jobs) {
        state.jobs = state.jobs.filter(j => j.syncJobId !== action.payload);
      }
    },
    setTitles: (state, action: PayloadAction<Title[]>) => {
      if (state.selectedJob) {
        state.selectedJob.titles = action.payload;
        const hasActualTitles = action.payload.some(title => title.name && title.name.trim() !== '');
        state.selectedJobWithNoTitles = !hasActualTitles;
      }
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

    // selectedJobRequestId guards: drop responses whose requestId no longer
    // matches the latest pending so rapid nav can't land stale data.
    builder.addCase(fetchJobDetails.pending, (state, action) => {
      state.selectedJobLoading = true;
      state.selectedJob = undefined;
      state.getJobDetailsError = undefined;
      state.selectedJobRequestId = action.meta.requestId;
    });
    builder.addCase(fetchJobDetails.fulfilled, (state, action) => {
      if (action.meta.requestId !== state.selectedJobRequestId) return;
      state.selectedJobLoading = false;
      state.selectedJob = action.payload;
      state.selectedJobWithNoTitles = !action.payload.titles || action.payload.titles.length === 0;
    });
    builder.addCase(fetchJobDetails.rejected, (state, action) => {
      if (action.meta.requestId !== state.selectedJobRequestId) return;
      state.selectedJobLoading = false;
      state.getJobDetailsError = action.error.message;
    });

    // getGroupDetails
    builder.addCase(getGroupDetails.pending, (state, action) => {
      state.selectedJobLoading = true;
      state.selectedJob = undefined;
      state.getJobDetailsError = undefined;
      state.selectedJobRequestId = action.meta.requestId;
    });
    builder.addCase(getGroupDetails.fulfilled, (state, action) => {
      if (action.meta.requestId !== state.selectedJobRequestId) return;
      state.selectedJobLoading = false;
      state.selectedJob = action.payload;
      state.selectedJobWithNoTitles = !action.payload.titles || action.payload.titles.length === 0;
    });
    builder.addCase(getGroupDetails.rejected, (state, action) => {
      if (action.meta.requestId !== state.selectedJobRequestId) return;
      state.selectedJobLoading = false;
      state.getJobDetailsError = action.error.message;
    });

    // getChannelDetails
    builder.addCase(getChannelDetails.pending, (state, action) => {
      state.selectedJobLoading = true;
      state.selectedJob = undefined;
      state.getJobDetailsError = undefined;
      state.selectedJobRequestId = action.meta.requestId;
    });
    builder.addCase(getChannelDetails.fulfilled, (state, action) => {
      if (action.meta.requestId !== state.selectedJobRequestId) return;
      state.selectedJobLoading = false;
      state.selectedJob = action.payload;
      state.selectedJobWithNoTitles = !action.payload.titles || action.payload.titles.length === 0;
    });
    builder.addCase(getChannelDetails.rejected, (state, action) => {
      if (action.meta.requestId !== state.selectedJobRequestId) return;
      state.selectedJobLoading = false;
      state.getJobDetailsError = action.error.message;
    });

    // patchJobDetails
    builder.addCase(patchJobDetails.pending, (state) => {
      state.patchJobDetailsResponse = undefined;
      state.patchJobDetailsError = undefined;
    });
    builder.addCase(patchJobDetails.fulfilled, (state, action) => {
      state.jobIdSet = "";
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
      state.approveJobsError = undefined;
    });
    builder.addCase(approveJobs.fulfilled, (state, action) => {
      state.approveJobsLoading = false;
      state.totalNumberOfApprovedJobs = action.payload.totalNumberOfApprovedJobs;
      state.totalNumberOfJobs = action.payload.totalNumberOfJobs;
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

    // fetchJobChanges – requestId-guarded against rapid job nav.
    builder.addCase(fetchJobChanges.pending, (state, action) => {
      state.selectedJobChangesLoading = true;
      state.selectedJobChanges = undefined;
      state.selectedJobChangesError = undefined;
      state.selectedJobChangesRequestId = action.meta.requestId;
    });
    builder.addCase(fetchJobChanges.fulfilled, (state, action) => {
      if (action.meta.requestId !== state.selectedJobChangesRequestId) return;
      state.selectedJobChangesLoading = false;
      state.selectedJobChanges = action.payload;
    });
    builder.addCase(fetchJobChanges.rejected, (state, action) => {
      if (action.meta.requestId !== state.selectedJobChangesRequestId) return;
      state.selectedJobChangesLoading = false;
      state.selectedJobChangesError = action.error.message;
    });
  }
});


export const { setJobs, setGetJobsError, setGetJobDetailsError, clearJob, clearJobsToDownload, updateJobOwnerFilterSuggestions, setApproveJobsLoading, setApproveJobsResponse, setTitles, setGeneratedTitlesYet, setJobId, setSelectedJobEnabled, setSelectedJobStatus, removeJobFromList } =
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
export const selectNumberOfApprovedJobs = (state: RootState) => state.jobs.totalNumberOfApprovedJobs;
export const selectNumberOfJobs = (state: RootState) => state.jobs.totalNumberOfJobs;
export const selectApproveJobsError = (state: RootState) => state.jobs.approveJobsError;

export const selectSelectedJobWithNoTitles = (state: RootState) => state.jobs.selectedJobWithNoTitles;
export const selectGeneratedTitlesYet = (state: RootState) => state.jobs.generatedTitlesYet;
export const selectJobIdSet = (state: RootState) => state.jobs.jobIdSet;

export default jobsSlice.reducer;
