// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getAllRoles } from './roles.api';

// Define a type for the slice state
export type Roles = {
  isJobOwnerReader: boolean;
  isJobOwnerEnabler: boolean;
  isJobOwnerDeleter: boolean;
  isJobOwnerWriter: boolean;
  isJobTenantReader: boolean;
  isJobTenantWriter: boolean;
  isSubmissionReviewer: boolean;
  isAutoApproverAdministrator: boolean;
  isCustomMembershipProviderAdministrator: boolean;
  isOperationsResetAdministrator: boolean;
  isGeneralSettingsAdministrator: boolean;
  isAIOnboardingChat: boolean;
  isAISettingsAdministrator: boolean;
  isAISyncJob: boolean;
  isTeamsChannelOnboarder: boolean;
  isFetchingRoles: boolean;
}

// Define the initial state using that type
const initialState: Roles = {
  isJobOwnerReader: false,
  isJobOwnerEnabler: false,
  isJobOwnerDeleter: false,
  isJobOwnerWriter: false,
  isJobTenantReader: false,
  isJobTenantWriter: false,
  isSubmissionReviewer: false,
  isAutoApproverAdministrator: false,
  isCustomMembershipProviderAdministrator: false,
  isOperationsResetAdministrator: false,
  isGeneralSettingsAdministrator: false,
  isAIOnboardingChat: false,
  isAISettingsAdministrator: false,
  isAISyncJob: false,
  isTeamsChannelOnboarder: false,
  isFetchingRoles: false,
};

export const rolesSlice = createSlice({
  name: 'roles',
  initialState,
  reducers: { },
  extraReducers: (builder) => {
    builder.addCase(getAllRoles.pending, (state) => {
        state.isFetchingRoles = true;
    });
    builder.addCase(getAllRoles.fulfilled, (state, action) => {
        Object.assign(state, action.payload);
        state.isFetchingRoles = false;
        console.log('Roles fetched successfully:', action.payload);
    });
    builder.addCase(getAllRoles.rejected, (state) => {
        state.isFetchingRoles = false;
    });
  }
});

export const selectIsFetchingRoles = (state: RootState) => state.roles.isFetchingRoles;
export const selectIsJobOwnerReader = (state: RootState) => state.roles.isJobOwnerReader;
export const selectIsJobOwnerEnabler = (state: RootState) => state.roles.isJobOwnerEnabler;
export const selectIsJobOwnerDeleter = (state: RootState) => state.roles.isJobOwnerDeleter;
export const selectIsJobTenantReader = (state: RootState) => state.roles.isJobTenantReader;
export const selectIsJobTenantWriter = (state: RootState) => state.roles.isJobTenantWriter;
export const selectIsSubmissionReviewer = (state: RootState) => state.roles.isSubmissionReviewer;
export const selectIsCustomMembershipProviderAdministrator = (state: RootState) => state.roles.isCustomMembershipProviderAdministrator;
export const selectIsOperationsResetAdministrator = (state: RootState) => state.roles.isOperationsResetAdministrator;
export const selectIsGeneralSettingsAdministrator = (state: RootState) => state.roles.isGeneralSettingsAdministrator;
export const selectIsAutoApproverAdministrator = (state: RootState) => state.roles.isAutoApproverAdministrator;
export const selectIsAIOnboardingChat = (state: RootState) => state.roles.isAIOnboardingChat;
export const selectIsAISettingsAdministrator = (state: RootState) => state.roles.isAISettingsAdministrator;
export const selectIsAISyncJob = (state: RootState) => state.roles.isAISyncJob;
export const selectIsTeamsChannelOnboarder = (state: RootState) => state.roles.isTeamsChannelOnboarder;

export const selectHasAccess = (state: RootState) => {
  return state.roles.isJobOwnerReader || state.roles.isJobOwnerWriter || state.roles.isJobTenantReader || state.roles.isJobTenantWriter;
};

export const selectHasJobWritePermissions = (state: RootState) => {
  return state.roles.isJobOwnerWriter || state.roles.isJobTenantWriter;
};

export const selectIsJobWriter = (state: RootState) => {
  return state.roles.isJobOwnerWriter || state.roles.isJobTenantWriter;
};

export const selectHasAdminCenterPermissions = (state: RootState) => {
  return state.roles.isCustomMembershipProviderAdministrator ||
          state.roles.isOperationsResetAdministrator ||
          state.roles.isGeneralSettingsAdministrator ||
          state.roles.isAutoApproverAdministrator ||
          state.roles.isAISettingsAdministrator;
};

export default rolesSlice.reducer;
