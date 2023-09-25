// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import { fetchSettingByKey, updateSetting } from './settings.api';
import type { RootState } from './store';
import { Setting } from '../models/Settings';

export interface SettingsState {
  selectedSettingLoading: boolean;
  selectedSetting: Setting | undefined;
  settings?: Setting[];
  getSettingsError: string | undefined;
}

const initialState: SettingsState = {
  selectedSettingLoading: false,
  selectedSetting: undefined,
  settings: undefined,
  getSettingsError: undefined
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
    builder.addCase(updateSetting.pending, (state) => {
      state.selectedSettingLoading = true;
      state.selectedSetting = undefined;
    });
    builder.addCase(updateSetting.fulfilled, (state, action) => {
      state.selectedSettingLoading = false;
      state.selectedSetting = action.payload;
    });
    builder.addCase(updateSetting.rejected, (state, action) => {
      state.selectedSettingLoading = false;
      state.getSettingsError = action.error.message;
    });
  },
});

export const { setSettings, setGetSettingsError } = settingsSlice.actions;

export const selectAllSettings = (state: RootState) => state.settings.settings;

export const selectSelectedSetting = (state: RootState) => state.settings.selectedSetting;

export const selectSelectedSettingLoading = (state: RootState) => state.settings.selectedSettingLoading;

export const selectGetSettingsError = (state: RootState) => state.settings.getSettingsError;

export const selectSettingByKey = (key: string) => (state: RootState) => state.settings.settings?.find(setting => setting.key === key);

export default settingsSlice.reducer;

