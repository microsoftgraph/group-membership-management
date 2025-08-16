// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import { fetchSettingByKey, fetchSettings, patchSetting, getSupportEmailAddress } from './settings.api';
import type { RootState } from './store';
import { Setting } from '../models/Setting';
import { SettingKey } from '../models/SettingKey';

export interface SettingsState {
  selectedSettingLoading: boolean;
  selectedSetting: Setting | undefined;
  settings?: Setting[];
  isLoading: boolean;
  error: string | undefined;
  patchSettingResponse: any | undefined;
  patchSettingError: string | undefined;
  isSaving: boolean;
  supportEmail: string;
  supportEmailLoading: boolean;
  supportEmailError: string | undefined;
}

const initialState: SettingsState = {
  selectedSettingLoading: false,
  selectedSetting: undefined,
  settings: undefined,
  isLoading: false,
  error: undefined,
  patchSettingResponse: undefined,
  patchSettingError: undefined,
  isSaving: false,
  supportEmail: '',
  supportEmailLoading: false,
  supportEmailError: undefined,
};

const settingsSlice = createSlice({
  name: 'settings',
  initialState,
  reducers: {
    setSettings: (state, action: PayloadAction<Setting[]>) => {
      state.settings = action.payload;
    },
  },
  extraReducers: (builder) => {
    builder.addCase(fetchSettings.pending, (state) => {
      state.isLoading = true;
      state.settings = undefined;
    });
    builder.addCase(fetchSettings.fulfilled, (state, action) => {
      state.isLoading = false;
      state.settings = action.payload;
    });
    builder.addCase(fetchSettings.rejected, (state, action) => {
      state.isLoading = false;
      state.error = action.error.message;
    });
    builder.addCase(fetchSettingByKey.pending, (state) => {
      state.selectedSettingLoading = true;
      state.selectedSetting = undefined;
    });
    builder.addCase(fetchSettingByKey.fulfilled, (state, action) => {
      state.selectedSettingLoading = false;
      state.selectedSetting = action.payload;
    });
    builder.addCase(fetchSettingByKey.rejected, (state, action) => {
      state.selectedSettingLoading = false;
      state.error = action.error.message;
    });
    builder.addCase(patchSetting.pending, (state) => {
      state.isSaving = true;
      state.selectedSettingLoading = true;
      state.selectedSetting = undefined;
    });
    builder.addCase(patchSetting.fulfilled, (state, action) => {
      state.isSaving = false;
      state.selectedSettingLoading = false;
      state.patchSettingResponse = action.payload;
      state.patchSettingError = undefined;
    });
    builder.addCase(patchSetting.rejected, (state, action) => {
      state.isSaving = false;
      state.selectedSettingLoading = false;
      state.error = action.error.message;
      state.patchSettingResponse = action.payload;
      state.patchSettingError = action.error.message;
    });
    builder.addCase(getSupportEmailAddress.pending, (state) => {
      state.supportEmailLoading = true;
      state.supportEmailError = undefined;
    });
    builder.addCase(getSupportEmailAddress.fulfilled, (state, action) => {
      state.supportEmailLoading = false;
      state.supportEmail = action.payload;
    });
    builder.addCase(getSupportEmailAddress.rejected, (state, action) => {
      state.supportEmailLoading = false;
      state.supportEmailError = action.error.message;
    });
  },
});

export const { setSettings } = settingsSlice.actions;

export const selectAllSettings = (state: RootState) => state.settings.settings;
export const selectError = (state: RootState) => state.settings.error;

export const selectDashboardUrl = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const dashboardSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.DashboardUrl);
  return dashboardSetting ? dashboardSetting.settingValue : undefined;
};

export const selectOutlookWarningUrl = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const outlookWarningSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.OutlookWarningUrl);
  return outlookWarningSetting ? outlookWarningSetting.settingValue : undefined;
};

export const selectPrivacyPolicyUrl = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const privacyPolicySetting = settingsArray.find((setting) => setting.settingKey === SettingKey.PrivacyPolicyUrl);
  return privacyPolicySetting ? privacyPolicySetting.settingValue : undefined;
};

export const selectUIUrl = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const uiSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.UIUrl);
  return uiSetting ? uiSetting.settingValue : undefined;
}

export const selectCanReviewOwnSubmissions = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const canReviewOwnSubmissionSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.CanReviewOwnSubmissions);
  return canReviewOwnSubmissionSetting ? canReviewOwnSubmissionSetting.settingValue === 'true' : undefined;
}

export const selectCreateGroupFeatureEnabled = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const createGroupFeatureEnabledSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.CreateGroupFeatureEnabled);
  return createGroupFeatureEnabledSetting ? createGroupFeatureEnabledSetting.settingValue === 'true' : undefined;
}

export const selectIsBusinessJustificationRequired = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const isBusinessJustificationRequiredSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.IsBusinessJustificationRequired);
  return isBusinessJustificationRequiredSetting ? isBusinessJustificationRequiredSetting.settingValue === 'true' : undefined;
}

export const selectIsDisclaimerEnabled = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const isDisclaimerEnabledSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.IsDisclaimerEnabled);
  return isDisclaimerEnabledSetting ? isDisclaimerEnabledSetting.settingValue === 'true' : undefined;
}

export const selectIsAutoApprovalForGroupBasedSyncsEnabled = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const isAutoApprovalForGroupBasedSyncsEnabledSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled);
  return isAutoApprovalForGroupBasedSyncsEnabledSetting ? isAutoApprovalForGroupBasedSyncsEnabledSetting.settingValue === 'true' : undefined;
}

export const selectIsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled);
  return isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledSetting ? isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledSetting.settingValue === 'true' : undefined;
}

export const selectIsAITitleEnabled = (state: RootState) => {
  const settingsArray = state.settings.settings;
  if (!settingsArray) {
    return undefined;
  }
  const isAITitleEnabledSetting = settingsArray.find((setting) => setting.settingKey === SettingKey.IsAITitleEnabled);
  return isAITitleEnabledSetting ? isAITitleEnabledSetting.settingValue === 'true' : undefined;
}

export const selectPatchSettingResponse = (state: RootState) => state.settings.patchSettingResponse;
export const selectPatchSettingError = (state: RootState) => state.settings.patchSettingError;

export const selectIsSaving = (state: RootState) => state.settings.isSaving;

export default settingsSlice.reducer;
export const selectSupportEmail = (state: RootState) => state.settings.supportEmail;
export const selectSupportEmailLoading = (state: RootState) => state.settings.supportEmailLoading;
export const selectSupportEmailError = (state: RootState) => state.settings.supportEmailError;
