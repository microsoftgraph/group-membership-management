// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { type Setting } from '../models/Settings';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';

export const fetchSettingByKey = createAsyncThunk<Setting, string, ThunkConfig>(
  'settings/fetchSettingByKey',
  async (key, { extra }) => {
    const { authenticationService } = extra;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    headers.append('Authorization', `Bearer ${token}`);

    const options = {
      method: 'GET',
      headers,
    };

    try {
      const response = await fetch(
        config.settings + `?key=${encodeURIComponent(key)}`,
        options
      ).then(async (response) => await response.json());

      const payload: Setting = response;
      return payload;
    } catch (error) {
      throw new Error('Failed to fetch setting data!');
    }
  }
);

export const updateSetting = createAsyncThunk<Setting, Setting, ThunkConfig>(
  'settings/updateSetting',
  async (data, {extra}) => {
    const { authenticationService } = extra;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    headers.append('Authorization', `Bearer ${token}`);
    headers.append('Content-Type', 'application/json');

    const options = {
      method: 'POST',
      headers,
      body: JSON.stringify(data.value),
    };

    try {
      const response = await fetch(
        config.settings + `/${encodeURIComponent(data.key)}`,
        options
      ).then(async (response) => await response.json());

      const payload: Setting = response;

      if (!response.ok) {
        throw new Error('Failed to update setting data!');
      }

      return payload;
    } catch (error) {
      throw new Error('Failed to update setting data!');
    }
  }
);
