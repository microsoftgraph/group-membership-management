// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import { UserProfile } from '../models/UserProfile';
import { RootState } from './store';
import { fetchMyProfile } from './userProfile.api';

export interface UserProfileState {
  profile: UserProfile | null;
  isLoading: boolean;
  error: string | null;
}

const initialState: UserProfileState = {
  profile: null,
  isLoading: false,
  error: null,
};

const userProfileSlice = createSlice({
  name: 'userProfile',
  initialState,
  reducers: {
    clearProfile: (state) => {
      state.profile = null;
      state.error = null;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchMyProfile.pending, (state) => {
        state.isLoading = true;
        state.error = null;
      })
      .addCase(fetchMyProfile.fulfilled, (state, action) => {
        state.isLoading = false;
        state.profile = action.payload;
      })
      .addCase(fetchMyProfile.rejected, (state, action) => {
        state.isLoading = false;
        state.error = action.error.message || 'Failed to fetch user profile';
      });
  },
});

export const { clearProfile } = userProfileSlice.actions;

// Selectors
export const selectUserProfile = (state: RootState) => state.userProfile.profile;
export const selectUserProfileLoading = (state: RootState) => state.userProfile.isLoading;

export default userProfileSlice.reducer;
