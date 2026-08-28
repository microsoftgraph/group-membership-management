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
  // View-only signals. The API reports these as true for administrators too, because
  // read-write implies read. The *Administrator flags remain the only "can edit" signal.
  isGeneralSettingsReader: boolean;
  isAutoApproverReader: boolean;
  isAISettingsReader: boolean;
  isCustomMembershipProviderReader: boolean;
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
  isGeneralSettingsReader: false,
  isAutoApproverReader: false,
  isAISettingsReader: false,
  isCustomMembershipProviderReader: false,
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
export const selectIsGeneralSettingsReader = (state: RootState) => state.roles.isGeneralSettingsReader;
export const selectIsAutoApproverReader = (state: RootState) => state.roles.isAutoApproverReader;
export const selectIsAISettingsReader = (state: RootState) => state.roles.isAISettingsReader;
export const selectIsCustomMembershipProviderReader = (state: RootState) => state.roles.isCustomMembershipProviderReader;

export const selectHasAccess = (state: RootState) => {
  return state.roles.isJobOwnerReader || state.roles.isJobOwnerWriter || state.roles.isJobTenantReader || state.roles.isJobTenantWriter;
};

export const selectHasJobWritePermissions = (state: RootState) => {
  return state.roles.isJobOwnerWriter || state.roles.isJobTenantWriter;
};

export const selectIsJobWriter = (state: RootState) => {
  return state.roles.isJobOwnerWriter || state.roles.isJobTenantWriter;
};

// Governs whether the Admin Center is reachable, not whether anything on it is editable.
// A reader-only holder reaches the page and sees every control disabled.
export const selectHasAdminCenterPermissions = (state: RootState) => {
  return state.roles.isCustomMembershipProviderAdministrator ||
          state.roles.isOperationsResetAdministrator ||
          state.roles.isGeneralSettingsAdministrator ||
          state.roles.isAutoApproverAdministrator ||
          state.roles.isAISettingsAdministrator ||
          state.roles.isGeneralSettingsReader ||
          state.roles.isAutoApproverReader ||
          state.roles.isAISettingsReader ||
          state.roles.isCustomMembershipProviderReader;
};

// True when the user can reach the Admin Center but cannot change anything on it.
export const selectHasReadOnlyAdminCenterAccess = (state: RootState) => {
  const canEditSomething = state.roles.isCustomMembershipProviderAdministrator ||
          state.roles.isOperationsResetAdministrator ||
          state.roles.isGeneralSettingsAdministrator ||
          state.roles.isAutoApproverAdministrator ||
          state.roles.isAISettingsAdministrator;

  return selectHasAdminCenterPermissions(state) && !canEditSomething;
};

export default rolesSlice.reducer;
