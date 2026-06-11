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

export const getProfilePhotoUsingId = createAsyncThunk<
  { photoUrl: string | undefined, displayName: string | undefined },
  { id: string, type: 'lastModifiedBy' | 'lastModifiedOnBehalfOf' },
  ThunkConfig
>(
  'profile/getProfilePhotoUsingId',
  async (input, {extra}) => {
    const { graphApi } = extra.apis;
    if (!input.id) return { photoUrl: '', displayName: '' };
    const photoUrl = await graphApi.getProfilePhotoUrlUsingUserId(input.id);
    const displayName = await graphApi.getUser(input.id);
    return { photoUrl, displayName };
  }
);