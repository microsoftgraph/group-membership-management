// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getProfile, getProfilePhoto } from './profile.api';

// Define a type for the slice state
export type ProfileState = {
  userPreferredLanguage?: string;
  userProfilePhoto?: string;
}

// Define the initial state using that type
const initialState: ProfileState = {
  userPreferredLanguage: undefined,
  userProfilePhoto: undefined
};

export const profileSlice = createSlice({
  name: 'profile',
  initialState,
  reducers: { },
  extraReducers: (builder) => {
    builder.addCase(getProfile.fulfilled, (state, action) => {
      state.userPreferredLanguage = action.payload;
    });
    builder.addCase(getProfilePhoto.fulfilled, (state, action) => {
      state.userProfilePhoto = action.payload;
    })
  }
});

export const selectProfile = (state: RootState) => state.profile;
export const selectProfilePhoto = (state: RootState) => state.profile.userProfilePhoto;
export default profileSlice.reducer;