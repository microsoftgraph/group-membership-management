// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import { fetchSettingByKey, patchSetting  } from './settings.api';
import type { RootState } from './store';
import { Setting } from '../models/Setting';

export interface SettingsState {
  selectedSettingLoading: boolean;
  selectedSetting: Setting | undefined;
  settings?: Setting[];
  getSettingsError: string | undefined;
  patchSettingResponse: any | undefined;
  patchSettingError: string | undefined;
  isSaving: boolean;
}

const initialState: SettingsState = {
  selectedSettingLoading: false,
  selectedSetting: undefined,
  settings: undefined,
  getSettingsError: undefined,
  patchSettingResponse: undefined,
  patchSettingError: undefined,
  isSaving: false,
};

const settingsSlice = createSlice({
  name: 'settings',
  initialState,
  reducers: {
    setSettings: (state, action: PayloadAction<Setting[]>) => {
      state.settings = action.payload;
    },
    setGetSettingsError: (state) => {
      state.getSettingsError = undefined;
    },
  },
  extraReducers: (builder) => {
    builder.addCase(fetchSettingByKey.pending, (state) => {
      state.selectedSettingLoading = true;
      state.selectedSetting = undefined;
    });
    builder.addCase(fetchSettingByKey.fulfilled, (state, action) => {
      state.selectedSettingLoading = false;
      state.selectedSetting = action.payload;
    });
    builder.addCase(fetchSettingByKey.rejected, (state, action) => {
      state.selectedSettingLoading = false;
      state.getSettingsError = action.error.message;
    });
    builder.addCase(patchSetting.pending, (state) => {
      state.isSaving = true;
      state.selectedSettingLoading = true;
      state.selectedSetting = undefined;
    });
    builder.addCase(patchSetting.fulfilled, (state, action) => {
      state.isSaving = false;
      state.selectedSettingLoading = false;
      state.selectedSetting = action.payload;
    });
    builder.addCase(patchSetting.rejected, (state, action) => {
      state.isSaving = false;
      state.selectedSettingLoading = false;
      state.getSettingsError = action.error.message;
    });
  },
});

export const { setSettings, setGetSettingsError } = settingsSlice.actions;

export const selectAllSettings = (state: RootState) => state.settings.settings;

export const selectPatchSettingResponse = (state: RootState) => state.settings.patchSettingResponse;
export const selectPatchSettingError = (state: RootState) => state.settings.patchSettingError;

export const selectIsSaving = (state: RootState) => state.settings.isSaving;

export default settingsSlice.reducer;

