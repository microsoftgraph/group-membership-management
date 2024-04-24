// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getAllRoles } from './roles.api';

// Define a type for the slice state
export type Roles = {
  isJobOwnerReader: boolean;
  isJobOwnerWriter: boolean;
  isJobTenantReader: boolean;
  isJobTenantWriter: boolean;
  isSubmissionReviewer: boolean;
  isHyperlinkAdministrator: boolean;
  isCustomMembershipProviderAdministrator: boolean;
}

// Define the initial state using that type
const initialState: Roles = {
  isJobOwnerReader: false,
  isJobOwnerWriter: false,
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

export const selectIsJobOwnerReader = (state: RootState) => state.roles.isJobOwnerReader;
export const selectIsJobOwnerWriter = (state: RootState) => state.roles.isJobOwnerWriter;
export const selectIsJobTenantReader = (state: RootState) => state.roles.isJobTenantReader;
export const selectIsJobTenantWriter = (state: RootState) => state.roles.isJobTenantWriter;
export const selectIsSubmissionReviewer = (state: RootState) => state.roles.isSubmissionReviewer;
export const selectIsHyperlinkAdministrator = (state: RootState) => state.roles.isHyperlinkAdministrator;
export const selectIsCustomMembershipProviderAdministrator = (state: RootState) => state.roles.isCustomMembershipProviderAdministrator;

export const selectHasAccess = (state: RootState) => {
  return state.roles.isJobOwnerReader || state.roles.isJobOwnerWriter || state.roles.isJobTenantReader || state.roles.isJobTenantWriter;
};

export const selectIsJobWriter = (state: RootState) => {
  return state.roles.isJobOwnerWriter || state.roles.isJobTenantWriter;
};

export default rolesSlice.reducer;