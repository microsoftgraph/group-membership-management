// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { ServiceStatuses } from '../models/ServiceStatuses';
import { fetchServiceStatus, processOperation } from './operations.api';
import { Operations } from '../models/Operations';
import type { RootState } from './store';

interface OperationsState {
  status: ServiceStatuses | null;
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
      .addCase(fetchServiceStatus.pending, (state) => {
        state.isLoading = true;
        state.error = null;
      })
      .addCase(fetchServiceStatus.fulfilled, (state, action: PayloadAction<ServiceStatuses>) => {
        state.status = action.payload;
        state.isLoading = false;
      })
      .addCase(fetchServiceStatus.rejected, (state) => {
        state.isLoading = false;
        state.error = 'Failed to fetch service status.';
      })
      .addCase(processOperation.pending, (state, action) => {
        state.isLoading = true;
        state.error = null;
        switch (action.meta.arg) {
          case Operations.Stop:
            state.status = ServiceStatuses.Stopping;
            break;
          case Operations.Reset:
            state.status = ServiceStatuses.Resetting;
            break;
          case Operations.Start:
            state.status = ServiceStatuses.Starting;
            break;
        }
      })
      .addCase(processOperation.fulfilled, (state, action) => {
        state.isLoading = false;
      })
      .addCase(processOperation.rejected, (state, action) => {
        state.isLoading = false;
        state.error = `Failed to process the ${action.meta.arg} operation.`;
      });
  }
});

export const { resetError } = operationsSlice.actions;

export const selectOperationStatus = (state: RootState) => state.operations.status;
export const selectOperationIsLoading = (state: RootState) => state.operations.isLoading;
export const selectOperationError = (state: RootState) => state.operations.error;

export default operationsSlice.reducer;
