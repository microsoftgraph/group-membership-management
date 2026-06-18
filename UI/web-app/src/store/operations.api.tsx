// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import axios from 'axios';
import { createAsyncThunk } from '@reduxjs/toolkit';
import { ServiceStatuses } from '../models/ServiceStatuses';
import { ThunkConfig, ThunkConfigWithErrors } from './store';
import { Operations } from '../models/Operations';

// Distinguishes a genuine backend outage from an auth/token failure.
export type ServiceStatusFailure = 'auth' | 'service';

export const fetchServiceStatus = createAsyncThunk<ServiceStatuses, void, ThunkConfigWithErrors<ServiceStatusFailure>>(
  'operations/fetchServiceStatus',
  async (_, { extra, rejectWithValue }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.operationsApi.fetchServiceStatus();
    } catch (error) {
      // Auth/token failures must NOT render the maintenance page: an expired
      // session is recovered via interactive re-auth.
      const isAuthFailure =
        (axios.isAxiosError(error) &&
          (error.response?.status === 401 || error.response?.status === 403)) ||
        (error instanceof Error && error.message.includes('No active account'));

      return rejectWithValue(isAuthFailure ? 'auth' : 'service');
    }
  }
);

export const processOperation = createAsyncThunk<void, Operations, ThunkConfig>(
  'operations/processOperation',
  async (operation, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      await gmmApi.operationsApi.processOperation(operation);
    } catch (error) {
      throw new Error(`Failed to process the ${operation} operation!`);
    }
  }
);

// Usage examples:
export const resetOperation = () => processOperation(Operations.Reset);
export const stopOperation = () => processOperation(Operations.Stop);
export const startOperation = () => processOperation(Operations.Start);