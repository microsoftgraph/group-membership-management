// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getAllRoles } from './roles.api';

// Define a type for the slice state
export type Roles = {
  isJobCreator: boolean;
  isJobTenantReader: boolean;
  isJobTenantWriter: boolean;
  isSubmissionReviewer: boolean;
  isHyperlinkAdministrator: boolean;
  isCustomMembershipProviderAdministrator: boolean;
}

// Define the initial state using that type
const initialState: Roles = {
  isJobCreator: false,
  isJobTenantReader: false,
  isJobTenantWriter: false,
  isSubmissionReviewer: false,
  isHyperlinkAdministrator: false,
  isCustomMembershipProviderAdministrator: false,
};

export const rolesSlice = createSlice({
  name: 'roles',
  initialState,
  reducers: { },
  extraReducers: (builder) => {
    builder.addCase(getAllRoles.fulfilled, (state, action) => {
      Object.assign(state, action.payload);
    });
  }
});

export const selectIsJobCreator = (state: RootState) => state.roles.isJobCreator;
export const selectIsJobTenantReader = (state: RootState) => state.roles.isJobTenantReader;
export const selectIsJobTenantWriter = (state: RootState) => state.roles.isJobTenantWriter;
export const selectIsSubmissionReviewer = (state: RootState) => state.roles.isSubmissionReviewer;
export const selectIsHyperlinkAdministrator = (state: RootState) => state.roles.isHyperlinkAdministrator;
export const selectIsCustomMembershipProviderAdministrator = (state: RootState) => state.roles.isCustomMembershipProviderAdministrator;

export default rolesSlice.reducer;