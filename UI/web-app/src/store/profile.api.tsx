// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ThunkConfig } from './store';

export const getProfile = createAsyncThunk<string, void, ThunkConfig>(
  'profile/getProfile',
  async (_, {getState, extra }) => {
    const { graphApi } = extra.apis;
    const user = getState().account.user;
    if (!user) return '';
    return await graphApi.getPreferredLanguage(user);
  }
);

export const getProfilePhoto = createAsyncThunk<string, void, ThunkConfig>(
  'profile/getProfilePhoto',
  async (_, {getState, extra}) => {
    const { graphApi } = extra.apis;
    const user = getState().account.user;
    if (!user) return '';
    return await graphApi.getProfilePhotoUrl(user);
  }
);
