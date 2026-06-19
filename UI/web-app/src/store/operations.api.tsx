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
      // session is recovered via interactive re-auth, and any other auth-layer
      // failure means we never reached the backend, so we cannot conclude it is
      // down. Only genuine service failures (5xx/network/unexpected) should
      // surface maintenance.
      const isHttpAuthFailure =
        axios.isAxiosError(error) &&
        (error.response?.status === 401 || error.response?.status === 403);

      // MsalAuthenticationService.getTokenAsync rethrows MSAL auth-layer errors
      // (e.g. interaction_in_progress when a redirect is already in flight, or a
      // non-interaction BrowserAuthError). Every MSAL AuthError carries a string
      // `errorCode`, which a backend AxiosError/Error does not, so this reliably
      // distinguishes a token-flow failure from a real outage.
      const isMsalAuthFailure =
        error instanceof Error &&
        typeof (error as { errorCode?: unknown }).errorCode === 'string';

      // getTokenAsync throws this plain Error (no errorCode) before any MSAL
      // call when the active account is missing, so it needs its own check.
      const isNoActiveAccount =
        error instanceof Error && error.message.includes('No active account');

      const isAuthFailure = isHttpAuthFailure || isMsalAuthFailure || isNoActiveAccount;

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