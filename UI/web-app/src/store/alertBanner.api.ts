// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { AlertBannerConfig } from '../models/AlertBannerConfig';
import { ThunkConfig } from './store';

export const fetchAlertBanner = createAsyncThunk<AlertBannerConfig, void, ThunkConfig>(
  'alertBanner/fetchAlertBanner',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.settings.getAlertBanner();
    } catch (error) {
      throw new Error('Failed to fetch alert banner configuration!');
    }
  }
);

export const patchAlertBanner = createAsyncThunk<AlertBannerConfig, AlertBannerConfig, ThunkConfig>(
  'alertBanner/patchAlertBanner',
  async (config, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.settings.patchAlertBanner(config);
    } catch (error) {
      throw new Error('Failed to update alert banner configuration!');
    }
  }
);
