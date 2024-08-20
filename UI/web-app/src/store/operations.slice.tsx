import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { ServiceStatuses } from '../models/ServiceStatuses';
import { fetchServiceStatus, processOperation } from './operations.api';
import { Operations } from '../models/Operations';
import type { RootState } from './store';

interface OperationsState {
  status: ServiceStatuses | null;
  displayStatus: ServiceStatuses | null;
  isLoading: boolean;
  error: string | null;
  isOperationInProgress: boolean;
}

const initialState: OperationsState = {
  status: null,
  displayStatus: null, 
  isLoading: false,
  error: null,
  isOperationInProgress: false,
};

const operationsSlice = createSlice({
  name: 'operations',
  initialState,
  reducers: {
    resetError(state) {
      state.error = null;
    },
    updateServiceStatus: (state, action) => {
      state.status = action.payload;
      state.displayStatus = action.payload;
    },
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
        if (state.status === ServiceStatuses.Stopped && state.displayStatus === ServiceStatuses.Resetting) {
          return;
        }
        state.displayStatus = action.payload; 
        state.isOperationInProgress = false;
      })
      .addCase(fetchServiceStatus.rejected, (state) => {
        state.isLoading = false;
        state.error = 'Failed to fetch service status.';
        state.isOperationInProgress = false;
      })
      .addCase(processOperation.pending, (state, action) => {
        state.isLoading = true;
        state.error = null;
        state.isOperationInProgress = true;
        switch (action.meta.arg) {
          case Operations.Stop:
            state.status = ServiceStatuses.Stopping;
            state.displayStatus = ServiceStatuses.Stopping;
            break;
          case Operations.Reset:
            state.status = ServiceStatuses.Resetting;
            state.displayStatus = ServiceStatuses.Resetting;
            break;
          case Operations.Start:
            state.status = ServiceStatuses.Starting;
            state.displayStatus = ServiceStatuses.Starting;
            break;
        }
      })
      .addCase(processOperation.fulfilled, (state) => {
        state.isLoading = false;
      })
      .addCase(processOperation.rejected, (state, action) => {
        state.isLoading = false;
        state.error = `Failed to process the ${action.meta.arg} operation.`;
        state.isOperationInProgress = false;
      });
  }
});

export const { resetError } = operationsSlice.actions;
export const { updateServiceStatus } = operationsSlice.actions;

export const selectOperationStatus = (state: RootState) => state.operations.status;
export const selectOperationDisplayStatus = (state: RootState) => state.operations.displayStatus; 
export const selectOperationIsLoading = (state: RootState) => state.operations.isLoading;
export const selectOperationError = (state: RootState) => state.operations.error;
export const selectOperationInProgress = (state: RootState) => state.operations.isOperationInProgress;

export default operationsSlice.reducer;
