// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Setting } from '../../models';
import { AlertBannerConfig } from '../../models/AlertBannerConfig';

export interface ISettingsApi {
  fetchSettings(): Promise<Setting[]>;
  fetchSettingByKey(settingKey: string): Promise<Setting>;
  patchSetting(setting: Setting): Promise<Setting>;
  getSupportEmailAddress(): Promise<string>;
  getDefaultAIPrompt(): Promise<string>;
  getAlertBanner(): Promise<AlertBannerConfig>;
  patchAlertBanner(config: AlertBannerConfig): Promise<AlertBannerConfig>;
};
