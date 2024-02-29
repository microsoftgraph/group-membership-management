// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getIsAdmin, getIsSubmissionReviewer, getIsTenantJobEditor } from './roles.api';

// Define a type for the slice state
export type Roles = {
  isAdmin?: boolean;
  isSubmissionReviewer?: boolean;
  isTenantJobEditor?: boolean;
}

// Define the initial state using that type
const initialState: Roles = {
  isAdmin: false,
  isSubmissionReviewer: false,
  isTenantJobEditor: false
};

export const rolesSlice = createSlice({
  name: 'roles',
  initialState,
  reducers: { },
  extraReducers: (builder) => {
    builder.addCase(getIsAdmin.fulfilled, (state, action) => {
      state.isAdmin = action.payload;
    });
    builder.addCase(getIsSubmissionReviewer.fulfilled, (state, action) => {
      state.isSubmissionReviewer = action.payload;
    });
    builder.addCase(getIsTenantJobEditor.fulfilled, (state, action) => {
      state.isTenantJobEditor = action.payload;
    });
  }
});

export const selectIsAdmin = (state: RootState) => state.roles.isAdmin;
export const selectIsSubmissionReviewer = (state: RootState) => state.roles.isSubmissionReviewer;
export const selectIsTenantJobEditor = (state: RootState) => state.roles.isTenantJobEditor;

export default rolesSlice.reducer;