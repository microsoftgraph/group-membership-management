// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { type Setting } from '../models/Setting';
import { ThunkConfig } from './store';

export const fetchSettings = createAsyncThunk<Setting[], void, ThunkConfig>(
  'settings/fetchSettings',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.settings.fetchSettings();
    } catch (error) {
      throw new Error('Failed to fetch settings data!');
    }
  }
);

export const fetchSettingByKey = createAsyncThunk<Setting, string, ThunkConfig>(
  'settings/fetchSettingByKey',
  async (key, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.settings.fetchSettingByKey(key);
    } catch (error) {
      throw new Error('Failed to fetch setting data!');
    }
  }
);

export const patchSetting = createAsyncThunk<Setting, Setting, ThunkConfig>(
  'settings/patchSetting',
  async (setting, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.settings.patchSetting(setting);
    } catch (error) {
      throw new Error('Failed to update setting data!');
    }
  }
);
