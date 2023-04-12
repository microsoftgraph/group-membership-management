import { createSlice, PayloadAction } from "@reduxjs/toolkit";
import { Job } from "../models/Job";
import type { RootState } from "./store";
import { fetchJobs } from "./jobs.api";

// Define a type for the slice state
export type JobsState = {
  loading: boolean;
  jobs?: Array<Job>;
};

// Define the initial state using that type
const initialState: JobsState = {
  loading: false,
  jobs: undefined,
};

export const jobsSlice = createSlice({
  name: "jobs",
  initialState,
  reducers: {
    setJobs: (state, action: PayloadAction<Array<Job>>) => {
      state.jobs = action.payload;
    },
  },
  extraReducers: (builder) => {
    builder.addCase(fetchJobs.pending, (state) => {
      state.loading = true;
    });
    builder.addCase(fetchJobs.fulfilled, (state, action) => {
      state.loading = false;
      state.jobs = action.payload;
    });
    builder.addCase(fetchJobs.rejected, (state) => {
      state.loading = false;
    });
  },
});

export const { setJobs } = jobsSlice.actions;
export const selectAllJobs = (state: RootState) => state.jobs.jobs;
export default jobsSlice.reducer;
