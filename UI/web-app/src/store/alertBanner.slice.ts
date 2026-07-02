// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { AlertBannerConfig, getDefaultAlertBannerConfig } from '../models/AlertBannerConfig';
import { fetchAlertBanner, patchAlertBanner } from './alertBanner.api';

export interface AlertBannerState {
  config: AlertBannerConfig;
  isLoading: boolean;
  isSaving: boolean;
  loaded: boolean;
  error: string | undefined;
  saveError: string | undefined;
}

const initialState: AlertBannerState = {
  config: getDefaultAlertBannerConfig(),
  isLoading: false,
  isSaving: false,
  loaded: false,
  error: undefined,
  saveError: undefined,
};

const alertBannerSlice = createSlice({
  name: 'alertBanner',
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder.addCase(fetchAlertBanner.pending, (state) => {
      state.isLoading = true;
      state.error = undefined;
    });
    builder.addCase(fetchAlertBanner.fulfilled, (state, action) => {
      state.isLoading = false;
      state.loaded = true;
      state.config = action.payload;
    });
    builder.addCase(fetchAlertBanner.rejected, (state, action) => {
      state.isLoading = false;
      state.error = action.error.message;
    });
    builder.addCase(patchAlertBanner.pending, (state) => {
      state.isSaving = true;
      state.saveError = undefined;
    });
    builder.addCase(patchAlertBanner.fulfilled, (state, action) => {
      state.isSaving = false;
      state.config = action.payload;
    });
    builder.addCase(patchAlertBanner.rejected, (state, action) => {
      state.isSaving = false;
      state.saveError = action.error.message;
    });
  },
});

export const selectAlertBannerConfig = (state: RootState) => state.alertBanner.config;
export const selectAlertBannerIsLoading = (state: RootState) => state.alertBanner.isLoading;
export const selectAlertBannerIsSaving = (state: RootState) => state.alertBanner.isSaving;
export const selectAlertBannerLoaded = (state: RootState) => state.alertBanner.loaded;
export const selectAlertBannerError = (state: RootState) => state.alertBanner.error;
export const selectAlertBannerSaveError = (state: RootState) => state.alertBanner.saveError;

export default alertBannerSlice.reducer;
