// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getProfile, getProfilePhoto, getProfilePhotoUsingId } from './profile.api';

export type UserProfile = {
  photoUrl: string | undefined;
  displayName: string | undefined;
};

// Define a type for the slice state
export type ProfileState = {
  userPreferredLanguage?: string;
  userProfilePhoto?: string;
  userProfilePhotoUsingId?: string;
  lastModifiedOnBehalfOfUserProfilePhoto?: string;
  userProfile: UserProfile | undefined;
  lastModifiedOnBehalfOfUserProfile: UserProfile | undefined;
}

// Define the initial state using that type
const initialState: ProfileState = {
  userPreferredLanguage: undefined,
  userProfilePhoto: undefined,
  userProfilePhotoUsingId: undefined,
  lastModifiedOnBehalfOfUserProfilePhoto: undefined,
  userProfile: undefined,
  lastModifiedOnBehalfOfUserProfile: undefined
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
    });
    builder.addCase(getProfilePhotoUsingId.fulfilled, (state, action) => {
      if (action.meta.arg.type === 'lastModifiedBy') {
        state.userProfile = {
          photoUrl: action.payload.photoUrl,
          displayName: action.payload.displayName
        };
      } else if (action.meta.arg.type === 'lastModifiedOnBehalfOf') {
        state.lastModifiedOnBehalfOfUserProfile = {
          photoUrl: action.payload.photoUrl,
          displayName: action.payload.displayName
        };
      }
    });
  }
});

export const selectProfile = (state: RootState) => state.profile;
export const selectProfilePhoto = (state: RootState) => state.profile.userProfilePhoto;
export const selectLastModifiedUserProfile = (state: RootState) => state.profile.userProfile;
export const selectLastModifiedOnBehalfOfUserProfile = (state: RootState) => state.profile.lastModifiedOnBehalfOfUserProfile;
export default profileSlice.reducer;