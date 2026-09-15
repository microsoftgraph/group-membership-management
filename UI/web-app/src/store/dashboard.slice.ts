// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { ApprovalActivity, DashboardSummary, OnBehalfOfActivity } from '../models';
import { fetchApprovalActivity, fetchDashboardSummary, fetchOnBehalfOfActivity } from './dashboard.api';
import type { RootState } from './store';

export interface DashboardState {
  summary: DashboardSummary | undefined;
  loading: boolean;
  error: string | undefined;
  approvalActivity: ApprovalActivity | undefined;
  approvalActivityLoading: boolean;
  approvalActivityError: string | undefined;
  approvalWindowDays: number;
  onBehalfOfActivity: OnBehalfOfActivity | undefined;
  onBehalfOfActivityLoading: boolean;
  onBehalfOfActivityError: string | undefined;
  onBehalfOfWindowDays: number;
}

const initialState: DashboardState = {
  summary: undefined,
  loading: false,
  error: undefined,
  approvalActivity: undefined,
  approvalActivityLoading: false,
  approvalActivityError: undefined,
  approvalWindowDays: 7,
  onBehalfOfActivity: undefined,
  onBehalfOfActivityLoading: false,
  onBehalfOfActivityError: undefined,
  onBehalfOfWindowDays: 7,
};

const dashboardSlice = createSlice({
  name: 'dashboard',
  initialState,
  reducers: {
    resetDashboardError(state) {
      state.error = undefined;
    },
    setApprovalWindowDays(state, action: PayloadAction<number>) {
      state.approvalWindowDays = action.payload;
    },
    setOnBehalfOfWindowDays(state, action: PayloadAction<number>) {
      state.onBehalfOfWindowDays = action.payload;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchDashboardSummary.pending, (state) => {
        state.loading = true;
        state.error = undefined;
      })
      .addCase(fetchDashboardSummary.fulfilled, (state, action: PayloadAction<DashboardSummary>) => {
        state.summary = action.payload;
        state.loading = false;
      })
      .addCase(fetchDashboardSummary.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? 'Failed to fetch the dashboard summary.';
      })
      .addCase(fetchApprovalActivity.pending, (state) => {
        state.approvalActivityLoading = true;
        state.approvalActivityError = undefined;
      })
      .addCase(fetchApprovalActivity.fulfilled, (state, action: PayloadAction<ApprovalActivity>) => {
        state.approvalActivity = action.payload;
        state.approvalActivityLoading = false;
        // Echo the window the API actually served so a clamped request is reflected in the picker.
        if (action.payload?.windowDays) {
          state.approvalWindowDays = action.payload.windowDays;
        }
      })
      .addCase(fetchApprovalActivity.rejected, (state, action) => {
        state.approvalActivityLoading = false;
        state.approvalActivityError = action.error.message ?? 'Failed to fetch the approval activity.';
      })
      .addCase(fetchOnBehalfOfActivity.pending, (state) => {
        state.onBehalfOfActivityLoading = true;
        state.onBehalfOfActivityError = undefined;
      })
      .addCase(fetchOnBehalfOfActivity.fulfilled, (state, action: PayloadAction<OnBehalfOfActivity>) => {
        state.onBehalfOfActivity = action.payload;
        state.onBehalfOfActivityLoading = false;
        // Echo the window the API actually served so a clamped request is reflected in the picker.
        if (action.payload?.windowDays) {
          state.onBehalfOfWindowDays = action.payload.windowDays;
        }
      })
      .addCase(fetchOnBehalfOfActivity.rejected, (state, action) => {
        state.onBehalfOfActivityLoading = false;
        state.onBehalfOfActivityError = action.error.message ?? 'Failed to fetch the on behalf of activity.';
      });
  },
});

export const { resetDashboardError, setApprovalWindowDays, setOnBehalfOfWindowDays } = dashboardSlice.actions;

export const selectDashboardSummary = (state: RootState) => state.dashboard.summary;
export const selectDashboardLoading = (state: RootState) => state.dashboard.loading;
export const selectDashboardError = (state: RootState) => state.dashboard.error;
export const selectApprovalActivity = (state: RootState) => state.dashboard.approvalActivity;
export const selectApprovalActivityLoading = (state: RootState) => state.dashboard.approvalActivityLoading;
export const selectApprovalActivityError = (state: RootState) => state.dashboard.approvalActivityError;
export const selectApprovalWindowDays = (state: RootState) => state.dashboard.approvalWindowDays;
export const selectOnBehalfOfActivity = (state: RootState) => state.dashboard.onBehalfOfActivity;
export const selectOnBehalfOfActivityLoading = (state: RootState) => state.dashboard.onBehalfOfActivityLoading;
export const selectOnBehalfOfActivityError = (state: RootState) => state.dashboard.onBehalfOfActivityError;
export const selectOnBehalfOfWindowDays = (state: RootState) => state.dashboard.onBehalfOfWindowDays;

export default dashboardSlice.reducer;
