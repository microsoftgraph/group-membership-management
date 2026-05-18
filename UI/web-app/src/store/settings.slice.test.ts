// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import settingsReducer, {
  setSettings,
  selectDashboardUrl,
  selectOutlookWarningUrl,
  selectPrivacyPolicyUrl,
  selectUIUrl,
  selectCanReviewOwnSubmissions,
  selectCreateGroupFeatureEnabled,
  selectIsBusinessJustificationRequired,
  selectIsDisclaimerEnabled,
  selectIsAutoApprovalForGroupBasedSyncsEnabled,
  selectIsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled,
  selectIsAITitleEnabled,
  SettingsState,
} from './settings.slice';
import { fetchSettings, fetchSettingByKey, patchSetting, getSupportEmailAddress } from './settings.api';
import { SettingKey } from '../models/SettingKey';
import type { Setting } from '../models/Setting';

const initial: SettingsState = settingsReducer(undefined, { type: '@@INIT' });

const makeSetting = (key: string, value: string): Setting => ({
  settingKey: key,
  settingValue: value,
} as Setting);

const buildRoot = (settings?: Setting[]) =>
  ({ settings: { ...initial, settings } } as any);

describe('settings.slice — reducers', () => {
  it('setSettings sets settings array', () => {
    const settings = [makeSetting('k', 'v')];
    expect(settingsReducer(initial, setSettings(settings)).settings).toEqual(settings);
  });
});

describe('settings.slice — extraReducers', () => {
  it('fetchSettings.pending sets loading', () => {
    const state = settingsReducer(initial, fetchSettings.pending('req1', undefined as any));
    expect(state.isLoading).toBe(true);
    expect(state.settings).toBeUndefined();
  });

  it('fetchSettings.fulfilled sets settings', () => {
    const settings = [makeSetting('k', 'v')];
    const state = settingsReducer(initial, fetchSettings.fulfilled(settings, 'req1', undefined as any));
    expect(state.isLoading).toBe(false);
    expect(state.settings).toEqual(settings);
  });

  it('fetchSettings.rejected sets error', () => {
    const state = settingsReducer(initial, fetchSettings.rejected(new Error('fail'), 'req1', undefined as any));
    expect(state.isLoading).toBe(false);
    expect(state.error).toBe('fail');
  });

  it('fetchSettingByKey.pending sets loading', () => {
    const state = settingsReducer(initial, fetchSettingByKey.pending('req1', '' as any));
    expect(state.selectedSettingLoading).toBe(true);
  });

  it('fetchSettingByKey.fulfilled sets setting', () => {
    const setting = makeSetting('k', 'v');
    const state = settingsReducer(initial, fetchSettingByKey.fulfilled(setting, 'req1', '' as any));
    expect(state.selectedSettingLoading).toBe(false);
    expect(state.selectedSetting).toEqual(setting);
  });

  it('fetchSettingByKey.rejected sets error', () => {
    const state = settingsReducer(initial, fetchSettingByKey.rejected(new Error('err'), 'req1', '' as any));
    expect(state.selectedSettingLoading).toBe(false);
    expect(state.error).toBe('err');
  });

  it('patchSetting.pending sets saving', () => {
    const state = settingsReducer(initial, patchSetting.pending('req1', {} as any));
    expect(state.isSaving).toBe(true);
  });

  it('patchSetting.fulfilled clears saving', () => {
    const state = settingsReducer(initial, patchSetting.fulfilled({}, 'req1', {} as any));
    expect(state.isSaving).toBe(false);
    expect(state.patchSettingResponse).toBeDefined();
  });

  it('patchSetting.rejected sets error', () => {
    const state = settingsReducer(initial, patchSetting.rejected(new Error('patch err'), 'req1', {} as any));
    expect(state.isSaving).toBe(false);
    expect(state.patchSettingError).toBe('patch err');
  });

  it('getSupportEmailAddress.pending sets loading', () => {
    const state = settingsReducer(initial, getSupportEmailAddress.pending('req1', undefined as any));
    expect(state.supportEmailLoading).toBe(true);
  });

  it('getSupportEmailAddress.fulfilled sets email', () => {
    const state = settingsReducer(initial, getSupportEmailAddress.fulfilled('support@ex.com', 'req1', undefined as any));
    expect(state.supportEmailLoading).toBe(false);
    expect(state.supportEmail).toBe('support@ex.com');
  });

  it('getSupportEmailAddress.rejected sets error', () => {
    const state = settingsReducer(initial, getSupportEmailAddress.rejected(new Error('no email'), 'req1', undefined as any));
    expect(state.supportEmailLoading).toBe(false);
    expect(state.supportEmailError).toBe('no email');
  });
});

describe('settings.slice — selectors', () => {
  it('selectDashboardUrl returns url when present', () => {
    const root = buildRoot([makeSetting(SettingKey.DashboardUrl, 'https://dashboard')]);
    expect(selectDashboardUrl(root)).toBe('https://dashboard');
  });

  it('selectDashboardUrl returns undefined when settings is undefined', () => {
    expect(selectDashboardUrl(buildRoot())).toBeUndefined();
  });

  it('selectDashboardUrl returns undefined when setting not found', () => {
    expect(selectDashboardUrl(buildRoot([makeSetting('Other', 'v')]))).toBeUndefined();
  });

  it('selectOutlookWarningUrl returns url', () => {
    const root = buildRoot([makeSetting(SettingKey.OutlookWarningUrl, 'https://outlook')]);
    expect(selectOutlookWarningUrl(root)).toBe('https://outlook');
  });

  it('selectOutlookWarningUrl returns undefined when not set', () => {
    expect(selectOutlookWarningUrl(buildRoot())).toBeUndefined();
  });

  it('selectPrivacyPolicyUrl returns url', () => {
    const root = buildRoot([makeSetting(SettingKey.PrivacyPolicyUrl, 'https://privacy')]);
    expect(selectPrivacyPolicyUrl(root)).toBe('https://privacy');
  });

  it('selectPrivacyPolicyUrl returns undefined when not set', () => {
    expect(selectPrivacyPolicyUrl(buildRoot())).toBeUndefined();
  });

  it('selectUIUrl returns url', () => {
    const root = buildRoot([makeSetting(SettingKey.UIUrl, 'https://ui')]);
    expect(selectUIUrl(root)).toBe('https://ui');
  });

  it('selectCanReviewOwnSubmissions returns true', () => {
    const root = buildRoot([makeSetting(SettingKey.CanReviewOwnSubmissions, 'true')]);
    expect(selectCanReviewOwnSubmissions(root)).toBe(true);
  });

  it('selectCanReviewOwnSubmissions returns false', () => {
    const root = buildRoot([makeSetting(SettingKey.CanReviewOwnSubmissions, 'false')]);
    expect(selectCanReviewOwnSubmissions(root)).toBe(false);
  });

  it('selectCreateGroupFeatureEnabled returns true', () => {
    const root = buildRoot([makeSetting(SettingKey.CreateGroupFeatureEnabled, 'true')]);
    expect(selectCreateGroupFeatureEnabled(root)).toBe(true);
  });

  it('selectIsBusinessJustificationRequired returns true', () => {
    const root = buildRoot([makeSetting(SettingKey.IsBusinessJustificationRequired, 'true')]);
    expect(selectIsBusinessJustificationRequired(root)).toBe(true);
  });

  it('selectIsDisclaimerEnabled returns true', () => {
    const root = buildRoot([makeSetting(SettingKey.IsDisclaimerEnabled, 'true')]);
    expect(selectIsDisclaimerEnabled(root)).toBe(true);
  });

  it('selectIsAutoApprovalForGroupBasedSyncsEnabled returns true', () => {
    const root = buildRoot([makeSetting(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, 'true')]);
    expect(selectIsAutoApprovalForGroupBasedSyncsEnabled(root)).toBe(true);
  });

  it('selectIsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled returns true', () => {
    const root = buildRoot([makeSetting(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, 'true')]);
    expect(selectIsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled(root)).toBe(true);
  });

  it('selectIsAITitleEnabled returns true', () => {
    const root = buildRoot([makeSetting(SettingKey.IsAITitleEnabled, 'true')]);
    expect(selectIsAITitleEnabled(root)).toBe(true);
  });

  it('selectIsAITitleEnabled returns undefined when not set', () => {
    expect(selectIsAITitleEnabled(buildRoot())).toBeUndefined();
  });
});
