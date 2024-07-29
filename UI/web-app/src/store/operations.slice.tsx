// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { OperationStatus } from '../models/OperationStatus';
import { fetchOperationStatus, stopOperation, restartOperation } from './operations.api';
import type { RootState } from './store';

interface OperationsState {
  status: OperationStatus | null;
  isLoading: boolean;
  error: string | null;
}

const initialState: OperationsState = {
  status: null,
  isLoading: false,
  error: null,
};

const operationsSlice = createSlice({
  name: 'operations',
  initialState,
  reducers: {
    resetError(state) {
      state.error = null;
    }
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchOperationStatus.pending, (state) => {
        state.isLoading = true;
        state.error = null;
      })
      .addCase(fetchOperationStatus.fulfilled, (state, action: PayloadAction<OperationStatus>) => {
        state.status = action.payload;
        state.isLoading = false;
      })
      .addCase(fetchOperationStatus.rejected, (state, action) => {
        state.isLoading = false;
        state.error = 'Failed to fetch operation status.';
      })
      .addCase(stopOperation.pending, (state) => {
        state.isLoading = true;
        state.status = OperationStatus.Stopping; 
      })
      .addCase(stopOperation.fulfilled, (state) => {
        state.isLoading = false;
        state.status = OperationStatus.Stopped; 
      })
      .addCase(stopOperation.rejected, (state, action) => {
        state.isLoading = false;
        state.error = 'Failed to stop the operation.';
      })
      .addCase(restartOperation.pending, (state) => {
        state.isLoading = true;
      })
      .addCase(restartOperation.fulfilled, (state) => {
        state.isLoading = false;
        state.status = OperationStatus.Running;
      })
      .addCase(restartOperation.rejected, (state, action) => {
        state.isLoading = false;
        state.error = 'Failed to restart the operation.';
      });
  }
});

export const { resetError } = operationsSlice.actions;

export const selectOperationStatus = (state: RootState) => state.operations.status;
export const selectOperationIsLoading = (state: RootState) => state.operations.isLoading;
export const selectOperationError = (state: RootState) => state.operations.error;

export default operationsSlice.reducer;