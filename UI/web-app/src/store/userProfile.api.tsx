// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { UserProfile } from '../models/UserProfile';
import { ThunkConfig } from './store';

/**
 * Fetches the current logged-in user's extended profile
 * This includes department, company, manager info for Copilot context
 */
export const fetchMyProfile = createAsyncThunk<UserProfile, void, ThunkConfig>(
  'userProfile/fetchMyProfile',
  async (_, { extra }) => {
    const { graphApi } = extra.apis;
    return await graphApi.getMyProfile();
  }
);
