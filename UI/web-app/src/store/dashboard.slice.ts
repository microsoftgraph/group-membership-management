// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { DashboardSummary } from '../models';
import { fetchDashboardSummary } from './dashboard.api';
import type { RootState } from './store';

export interface DashboardState {
  summary: DashboardSummary | undefined;
  loading: boolean;
  error: string | undefined;
}

const initialState: DashboardState = {
  summary: undefined,
  loading: false,
  error: undefined,
};

const dashboardSlice = createSlice({
  name: 'dashboard',
  initialState,
  reducers: {
    resetDashboardError(state) {
      state.error = undefined;
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
      });
  },
});

export const { resetDashboardError } = dashboardSlice.actions;

export const selectDashboardSummary = (state: RootState) => state.dashboard.summary;
export const selectDashboardLoading = (state: RootState) => state.dashboard.loading;
export const selectDashboardError = (state: RootState) => state.dashboard.error;

export default dashboardSlice.reducer;
