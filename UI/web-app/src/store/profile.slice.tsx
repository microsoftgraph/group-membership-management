// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getProfile } from './profile.api';

// Define a type for the slice state
export type ProfileState = {
  loading: boolean;
  userPreferredLanguage?: string;
}

// Define the initial state using that type
const initialState: ProfileState = {
  loading: false,
  userPreferredLanguage: ""
};

export const profileSlice = createSlice({
  name: 'profile',
  initialState,
  reducers: {
    setProfile: (state, action: PayloadAction<string>) => {
      state.userPreferredLanguage = action.payload;
    }
  },
  extraReducers: (builder) => {
    builder.addCase(getProfile.fulfilled, (state, action) => {
      state.loading = false;
      state.userPreferredLanguage = action.payload;
    });
  }
});

export const { setProfile } = profileSlice.actions;
export const selectProfile = (state: RootState) => state.profile;
export default profileSlice.reducer;