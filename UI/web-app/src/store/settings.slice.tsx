// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { fetchSettings } from './settings.api';
import type { RootState } from './store';
import { type Settings } from '../models/Settings';

// Define a type for the slice state
export interface SettingsState {
  settingsLoading: boolean;
  settings?: Settings[];
  getSettingsError: string | undefined;
}

// Define the initial state using that type
const initialState: SettingsState = {
    settingsLoading: false,
    settings: undefined,
    getSettingsError: undefined
};

export const settingsSlice = createSlice({
  name: 'settings',
  initialState,
  reducers: {
    setSettings: (state, action: PayloadAction<Settings[]>) => {
      state.settings = action.payload;
    },
    setGetSettingsError: (state) => {
      state.getSettingsError = undefined;
    },
  },
  extraReducers: (builder) => {
    builder.addCase(fetchSettings.pending, (state) => {
      state.settingsLoading = true;
    });
    // builder.addCase(fetchSettings.fulfilled, (state, action) => {
    //   state.settingsLoading = false;
    //   state.settings = action.payload.settings;
    // });
    builder.addCase(fetchSettings.rejected, (state, action) => {
      state.settingsLoading = false;
      state.getSettingsError = action.error.message;
    });
  },
});

export const { setSettings, setGetSettingsError } =
settingsSlice.actions;

export const selectAllSettings = (state: RootState) => state.settings.settings;

export default settingsSlice.reducer;
